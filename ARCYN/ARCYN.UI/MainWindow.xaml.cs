using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ARCYN.UI.Models;

namespace ARCYN.UI;

public partial class MainWindow : Window
{
    private List<ModeConfig> _modes = [];
    private LoadingWindow? _loadingWindow;
    private MatrixOverlay? _matrix;
    private const string ConfigFileName = "protocoll.json";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableAcrylic(hwnd, 0xCC000000);

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        LoadConfig();
        _matrix = new MatrixOverlay(MatrixCanvas);
        _matrix.Start();
        FocusButton();
    }

    private void LoadConfig()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARCYN", ConfigFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName),
            Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) ?? ".", ConfigFileName)
        };

        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var json = File.ReadAllText(p);
                var modes = JsonSerializer.Deserialize<List<ModeConfig>>(json);
                _modes = modes?.Where(m => !string.IsNullOrWhiteSpace(m.Label)).ToList() ?? [];
                break;
            }
            catch { }
        }

        if (_modes.Count == 0)
            _modes = GetDefaultModes();

        ModeItems.ItemsSource = _modes;
        FocusButton();
    }

    private static List<ModeConfig> GetDefaultModes() =>
    [
        new() { Label = "STUDY", Command = "cmd", Args = ["/c", "start https://ncert.nic.in && start ms-clock: && start https://www.selfstudys.com/"] },
        new() { Label = "CODE", Command = "cmd", Args = ["/c", "start wt.exe && start D:\\ && start https://chat.openai.com"] },
        new() { Label = "DESIGN", Command = "cmd", Args = ["/c", "start https://www.canva.com && start https://www.pinterest.com"] },
        new() { Label = "BROWSE", Command = "cmd", Args = ["/c", "start https://www.google.com"] },
        new() { Label = "TERM", Command = "wt.exe", Args = [] },
        new() { Label = "FILES", Command = "explorer.exe", Args = [] }
    ];

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ModeConfig mode })
        {
            _matrix?.Stop();
            _loadingWindow = new LoadingWindow(mode, _modes.IndexOf(mode)) { Owner = this };
            _loadingWindow.Show();
            Hide();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter:
                if (FocusManager.GetFocusedElement(this) is Button btn)
                    ModeButton_Click(btn, new RoutedEventArgs());
                break;
        }
    }

    private void FocusButton()
    {
        Dispatcher.BeginInvoke(() =>
        {
            FindVisualChild<Button>(ModeItems)?.Focus();
        }, DispatcherPriority.ContextIdle);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}
