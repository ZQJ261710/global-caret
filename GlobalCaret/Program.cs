using System.Windows;

namespace GlobalCaret;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "GlobalCaret.SingleInstance", out bool firstInstance);
        if (!firstInstance) return;

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new CaretOverlay());
    }
}
