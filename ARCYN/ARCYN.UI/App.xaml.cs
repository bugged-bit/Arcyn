using System.Windows;

namespace ARCYN.UI;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var consoleWnd = NativeMethods.GetConsoleWindow();
        if (consoleWnd != IntPtr.Zero)
            NativeMethods.ShowWindowAsync(consoleWnd, NativeMethods.SW_HIDE);

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
