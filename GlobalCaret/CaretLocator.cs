using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GlobalCaret;

internal sealed record CaretPosition(double X, double Y, double Height);

internal static class CaretLocator
{
    public static CaretPosition? Locate()
    {
        // UI Automation exposes virtual carets used by browsers and modern editors.
        // Password controls are deliberately excluded.
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null)
            {
                if (focused.Current.IsPassword) return null;
                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
                {
                    var selections = ((TextPattern)pattern).GetSelection();
                    if (selections.Length == 1 &&
                        selections[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selections[0], TextPatternRangeEndpoint.End) == 0)
                    {
                        var rectangles = selections[0].GetBoundingRectangles();
                        if (rectangles.Length > 0 && IsUsable(rectangles[0]))
                        {
                            var rect = rectangles[0];
                            return new CaretPosition(rect.Left, rect.Top, rect.Height);
                        }
                    }
                }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }

        // Native edit controls often expose a caret only through Win32.
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.hwndCaret == IntPtr.Zero) return null;
        var point = new NativePoint { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        if (!ClientToScreen(info.hwndCaret, ref point)) return null;
        double height = info.rcCaret.Bottom - info.rcCaret.Top;
        return height is > 3 and < 200 ? new CaretPosition(point.X, point.Y, height) : null;
    }

    private static bool IsUsable(Rect rect) => !rect.IsEmpty &&
        double.IsFinite(rect.Left) && double.IsFinite(rect.Top) &&
        rect.Height is > 3 and < 200 && rect.Width < 100;

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
}
