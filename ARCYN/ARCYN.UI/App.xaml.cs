using System.IO;
using System.Windows;

namespace ARCYN.UI;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "arcyn_launch.log");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            try
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] FATAL: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}{Environment.NewLine}");
            }
            catch { }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] DISPATCHER: {args.Exception.Message}{Environment.NewLine}{args.Exception.StackTrace}{Environment.NewLine}");
            }
            catch { }

            args.Handled = true;
            Current.Shutdown(-1);
        };

        var consoleWnd = NativeMethods.GetConsoleWindow();
        if (consoleWnd != IntPtr.Zero)
            NativeMethods.ShowWindowAsync(consoleWnd, NativeMethods.SW_HIDE);

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
