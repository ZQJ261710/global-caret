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
    private readonly DispatcherTimer _frameTimer;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Thread _locatorThread;
    private CaretPosition? _latest;
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
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _pauseItem = new Forms.ToolStripMenuItem("暂停效果");
        _pauseItem.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseItem.Text = _paused ? "恢复效果" : "暂停效果";
            InvalidateVisual();
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

        _locatorThread = new Thread(LocateLoop) { IsBackground = true, Name = "Caret locator" };
        _locatorThread.Start();
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frameTimer.Tick += (_, _) => Tick();
        _frameTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        nint style = GetWindowLongPtr(handle, -20);
        SetWindowLongPtr(handle, -20, style | 0x20 | 0x80 | 0x08000000);
    }

    private void LocateLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { Volatile.Write(ref _latest, CaretLocator.Locate()); }
            catch (Exception) { Volatile.Write(ref _latest, null); }
            Thread.Sleep(30);
        }
    }

    private void Tick()
    {
        bool wasVisible = _target != null;
        var current = _paused ? null : Volatile.Read(ref _latest);
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
        if (_target != null || wasVisible) InvalidateVisual();
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
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _stop.Dispose();
        base.OnClosing(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);
}
