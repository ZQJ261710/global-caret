using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace GlobalCaret;

internal sealed record CaretPosition(double X, double Y, double Height);

internal sealed class CaretLocator : IDisposable
{
    private readonly UIA3Automation _automation = new();

    public CaretPosition? Locate()
    {
        // A failed focus/password query must never fall through to a native caret.
        try
        {
            var focused = _automation.FocusedElement();
            if (focused == null || focused.Properties.IsPassword.Value) return null;

            if (focused.Patterns.Text2.TryGetPattern(out var text2))
            {
                var range = text2.GetCaretRange(out bool active);
                if (active && FirstCaretRectangle(range) is { } position) return position;
            }

            if (focused.Patterns.Text.TryGetPattern(out var text))
            {
                var selection = text.GetSelection();
                if (selection.Length == 1 &&
                    selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End) == 0 &&
                    FirstCaretRectangle(selection[0]) is { } position)
                    return position;
            }

            // Only use Win32 after UIA has positively identified the focused control.
            return LocateNative();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static CaretPosition? FirstCaretRectangle(ITextRange range)
    {
        foreach (var rect in range.GetBoundingRectangles())
        {
            if (rect.Height is > 3 and < 200 && rect.Width is >= 0 and < 100 &&
                double.IsFinite(rect.Left) && double.IsFinite(rect.Top))
                return new CaretPosition(rect.Left, rect.Top, rect.Height);
        }
        return null;
    }

    private static CaretPosition? LocateNative()
    {
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.hwndCaret == IntPtr.Zero || info.hwndFocus == IntPtr.Zero)
            return null;

        // ES_PASSWORD catches native edits even if a UIA provider misreports them.
        var className = new char[32];
        int length = GetClassName(info.hwndFocus, className, className.Length);
        if (length == 0) return null;
        var focusClass = new string(className, 0, length);
        if ((focusClass.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
             focusClass.StartsWith("WindowsForms10.Edit.", StringComparison.OrdinalIgnoreCase)) &&
            (GetWindowLongPtr(info.hwndFocus, -16).ToInt64() & 0x20) != 0)
            return null;

        var point = new NativePoint { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        if (!ClientToScreen(info.hwndCaret, ref point)) return null;
        double height = info.rcCaret.Bottom - info.rcCaret.Top;
        return height is > 3 and < 200 ? new CaretPosition(point.X, point.Y, height) : null;
    }

    public void Dispose() => _automation.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeRect rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);
}
