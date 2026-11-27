using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;

namespace GlobalCaret;

internal sealed class CaretOverlay : Window
{
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly DispatcherTimer _frameTimer;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Thread _locatorThread;
    private readonly WinEventProc _winEventProc;
    private readonly List<IntPtr> _hooks = new();
    private IntPtr _handle;
    private (int X, int Y, int Width, int Height) _bounds;
    private CaretPosition? _latest;
    private long _latestAt;
    private CaretPosition? _target;
    private double _trailX;
    private double _trailY;
    private bool _paused;
    private bool _hasTrail;
    private readonly DateTime _started = DateTime.UtcNow;

    public CaretOverlay()
    {
        Title = "Global Caret";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;
        Opacity = 0;

        _pauseItem = new Forms.ToolStripMenuItem("暂停效果");
        _pauseItem.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseItem.Text = _paused ? "恢复效果" : "暂停效果";
            _wake.Set();
            Dispatcher.BeginInvoke(UpdateVisual);
        };
        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Dispatcher.Invoke(Close);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(exitItem);
        _tray = new Forms.NotifyIcon
        {
            Text = "Global Caret · 右键暂停或退出",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _pauseItem.PerformClick();

        _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frameTimer.Tick += (_, _) => Tick();
        _winEventProc = OnWinEvent;
        _locatorThread = new Thread(LocateLoop) { IsBackground = true, Name = "Caret locator" };
        _locatorThread.SetApartmentState(ApartmentState.MTA);
        _locatorThread.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        nint style = GetWindowLongPtr(_handle, -20);
        SetWindowLongPtr(_handle, -20, style | 0x20 | 0x80 | 0x08000000);
        // Focus changes and native caret moves wake the locator immediately.
        _hooks.Add(SetWinEventHook(0x8005, 0x8005, IntPtr.Zero, _winEventProc, 0, 0, 0x0002));
        _hooks.Add(SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, _winEventProc, 0, 0, 0x0002));
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (eventType == 0x8005 || objectId == -8) _wake.Set();
    }

    private void LocateLoop()
    {
        using var locator = new CaretLocator();
        WaitHandle[] waits = [_stop.Token.WaitHandle, _wake];
        while (!_stop.IsCancellationRequested)
        {
            CaretPosition? position;
            try { position = locator.Locate(); }
            catch (Exception) { position = null; }
            var previous = Volatile.Read(ref _latest);
            Volatile.Write(ref _latest, position);
            Volatile.Write(ref _latestAt, Environment.TickCount64);
            if (position != previous)
                Dispatcher.BeginInvoke(UpdateVisual);
            if (WaitHandle.WaitAny(waits, position == null ? 300 : 100) == 0) break;
        }
    }

    private void UpdateVisual()
    {
        if (!_frameTimer.IsEnabled) _frameTimer.Start();
        Tick();
    }

    private void Tick()
    {
        bool wasVisible = _target != null;
        var current = _paused || Environment.TickCount64 - Volatile.Read(ref _latestAt) > 1000
            ? null : Volatile.Read(ref _latest);
        if (current == null)
        {
            _target = null;
            _hasTrail = false;
        }
        else
        {
            if (_target == null || Math.Abs(current.X - _target.X) > 250 || Math.Abs(current.Y - _target.Y) > 120)
                _hasTrail = false;
            _target = current;
            if (!_hasTrail)
            {
                _trailX = current.X;
                _trailY = current.Y;
                _hasTrail = true;
            }
            _trailX += (current.X - _trailX) * 0.30;
            _trailY += (current.Y - _trailY) * 0.30;
        }
        if (_target != null)
        {
            // Win32 coordinates are physical pixels. Keep the transparent surface local
            // to the caret so mixed-DPI desktops do not require a giant WPF window.
            int x = (int)Math.Floor(Math.Min(current!.X, _trailX) - 24);
            int y = (int)Math.Floor(Math.Min(current.Y, _trailY) - 24);
            int right = (int)Math.Ceiling(Math.Max(current.X, _trailX) + 30);
            int bottom = (int)Math.Ceiling(Math.Max(current.Y + current.Height, _trailY + current.Height / 2) + 24);
            var bounds = (x, y, right - x, bottom - y);
            if (_handle != IntPtr.Zero && bounds != _bounds)
            {
                SetWindowPos(_handle, new IntPtr(-1), x, y, bounds.Item3, bounds.Item4, 0x0010 | 0x0004);
                _bounds = bounds;
            }
            Opacity = 1;
            InvalidateVisual();
        }
        else if (wasVisible)
        {
            Opacity = 0;
            InvalidateVisual();
            _frameTimer.Stop();
        }
        else _frameTimer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var caret = _target;
        if (caret == null) return;

        Point top = PointFromScreen(new Point(caret.X, caret.Y));
        Point bottom = PointFromScreen(new Point(caret.X, caret.Y + caret.Height));
        Point trail = PointFromScreen(new Point(_trailX, _trailY + caret.Height / 2));
        double height = Math.Max(6, bottom.Y - top.Y);
        double width = Math.Max(3, PointFromScreen(new Point(caret.X + 6, caret.Y)).X - top.X);
        var center = new Point(top.X + width / 2, top.Y + height / 2);
        double phase = (DateTime.UtcNow - _started).TotalSeconds * 0.12;
        Color first = Rainbow(phase);
        Color second = Rainbow(phase + 0.28);
        Color third = Rainbow(phase + 0.58);

        if (Math.Abs(trail.X - center.X) + Math.Abs(trail.Y - center.Y) > 2)
        {
            var trailColor = Color.FromArgb(110, first.R, first.G, first.B);
            dc.DrawLine(new Pen(new SolidColorBrush(trailColor), width * 0.65), trail, center);
        }

        var glow = new SolidColorBrush(Color.FromArgb(35, first.R, first.G, first.B));
        dc.DrawRoundedRectangle(glow, null, new Rect(top.X - width * 0.7, top.Y - 2, width * 2.4, height + 4), width, width);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new(first, 0), new(second, 0.5), new(third, 1)
            }
        };
        dc.DrawRoundedRectangle(brush, null, new Rect(top.X, top.Y, width, height), width * 0.25, width * 0.25);
    }

    private static Color Rainbow(double phase)
    {
        phase = ((phase % 1) + 1) % 1;
        double value = phase * 6;
        byte hue = (byte)Math.Round((value - Math.Floor(value)) * 255);
        return (int)value switch
        {
            0 => Color.FromRgb(255, hue, 75),
            1 => Color.FromRgb((byte)(255 - hue), 255, 75),
            2 => Color.FromRgb(75, 255, hue),
            3 => Color.FromRgb(75, (byte)(255 - hue), 255),
            4 => Color.FromRgb(hue, 75, 255),
            _ => Color.FromRgb(255, 75, (byte)(255 - hue))
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _frameTimer.Stop();
        _stop.Cancel();
        _wake.Set();
        foreach (var hook in _hooks) if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        bool locatorStopped = _locatorThread.Join(300);
        if (locatorStopped) _wake.Dispose();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        if (locatorStopped) _stop.Dispose();
        base.OnClosing(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
