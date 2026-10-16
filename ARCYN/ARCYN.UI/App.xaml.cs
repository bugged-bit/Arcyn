using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ARCYN.UI.Models;
using ARCYN.UI.Services;
namespace ARCYN.UI;

public partial class App : Application
{
    private SingleInstance? _instance;
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var terminalCommand = e.Args.Any(a => a is "--help" or "--version" or "--config-path" or "--setup");
        try
        {
            string? explicitPath = null;
            var args = e.Args;
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "--config")
                {
                    if (++i >= args.Length) throw new ArgumentException("--config needs a file path.");
                    explicitPath = args[i];
                }
                else if (args[i] is not ("--help" or "--version" or "--config-path" or "--setup"))
                    throw new ArgumentException("Unknown option: " + args[i]);
            var service = new ConfigService(explicitPath);
            if (args.Any(a => a is "--help" or "--version" or "--config-path" or "--setup"))
            {
                AttachConsole(unchecked((uint)-1));
                if (args.Contains("--help")) Console.WriteLine("ARCYN: --help | --version | --config-path | --config <file> | --setup (terminal setup)");
                else if (args.Contains("--version")) Console.WriteLine("ARCYN " + typeof(App).Assembly.GetName().Version);
                else if (args.Contains("--config-path")) Console.WriteLine(service.GetOrCreatePath());
                else
                {
                    using var setupInstance = new SingleInstance(() => { });
                    if (!setupInstance.IsFirst) { Console.Error.WriteLine("Close ARCYN before terminal setup to preserve active edits."); Shutdown(1); return; }
                    RunCliSetup(service);
                }
                Shutdown(); return;
            }
            _instance = new SingleInstance(() => Dispatcher.BeginInvoke(() => {
                if (MainWindow is not { } window) return;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                while (window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible) is { } child) window = child;
                window.Activate(); window.Focus();
            }));
            if (!_instance.IsFirst) { Shutdown(); return; }
            AppDomain.CurrentDomain.UnhandledException += (_, a) => LogService.WriteStatic("Fatal: {0}", a.ExceptionObject);
            DispatcherUnhandledException += (_, a) => {
                LogService.WriteStatic("Dispatcher: {0}", a.Exception);
                MessageBox.Show(a.Exception.Message, "ARCYN", MessageBoxButton.OK, MessageBoxImage.Error);
                a.Handled = true; Shutdown(-1);
            };
            var config = service.Load();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            MainWindow = config == null ? new SetupWindow(service) : new MainWindow(service, config);
            MainWindow.Title = "ARCYN";
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            LogService.WriteStatic("Startup: {0}", ex);
            if (terminalCommand) { AttachConsole(unchecked((uint)-1)); Console.Error.WriteLine(ex.Message); }
            else MessageBox.Show(ex.Message, "ARCYN — configuration preserved", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint process);

    private static void RunCliSetup(ConfigService service)
    {
        var config = service.Load() ?? new ArcynConfig();
        Console.WriteLine("ARCYN terminal setup. Existing workspaces are retained. Blank name finishes.");
        var added = false;
        while (true)
        {
            Console.Write("Workspace name: ");
            var name = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(name)) break;
            var mode = new ModeConfig { Name = name };
            Console.Write("Description: "); mode.Description = Console.ReadLine() ?? "";
            Console.WriteLine("Enter one executable, shortcut, folder, URL or protocol per line. Blank line finishes.");
            while (Console.ReadLine() is { Length: > 0 } cmd) mode.Targets.Add(new TargetConfig { Cmd = cmd.Trim() });
            config.Modes.Add(mode); added = true;
        }
        if (added) { service.Save(config); Console.WriteLine("Saved: " + service.GetOrCreatePath()); }
    }
}
