using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
    private ParticleEngine? _particles;
    private DispatcherTimer? _idleTimer;
    private const string ConfigFileName = "protocoll.json";
    private const int IdleTimeoutSeconds = 15;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
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

        _particles = new ParticleEngine(MatrixCanvas);
        _particles.Start();

        await PlayStartupSequence();
        StartIdleTimer();
    }

    private async Task PlayStartupSequence()
    {
        await Task.Delay(250);

        AnimateScaleX(SeparatorTop, 0, 1, 0.25);
        AnimateScaleX(SeparatorBottom, 0, 1, 0.25);

        await Task.Delay(80);

        for (int i = 0; i < ModeItems.Items.Count; i++)
        {
            var container = ModeItems.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is FrameworkElement fe)
            {
                var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)));
                anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                fe.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            await Task.Delay(70);
        }

        var footerAnim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)));
        FooterHint.BeginAnimation(UIElement.OpacityProperty, footerAnim);

        Dispatcher.BeginInvoke(() =>
        {
            if (ModeItems.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement c)
                FindVisualChild<Button>(c)?.Focus();
        }, DispatcherPriority.ContextIdle);
    }

    private static void AnimateScaleX(FrameworkElement target, double from, double to, double duration)
    {
        var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(duration)));
        anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        target.RenderTransform = new ScaleTransform(from, 1);
        target.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
    }

    private void StartIdleTimer()
    {
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(IdleTimeoutSeconds) };
        _idleTimer.Tick += (_, _) => CloseWithFade();
        _idleTimer.Start();

        PreviewMouseMove += (_, _) => ResetIdleTimer();
        PreviewKeyDown += (_, _) => ResetIdleTimer();
    }

    private void ResetIdleTimer()
    {
        _idleTimer?.Stop();
        _idleTimer?.Start();
    }

    private void CloseWithFade()
    {
        _idleTimer?.Stop();
        _matrix?.Stop();
        _particles?.Stop();

        var anim = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(0.2))
        };
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
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
            SelectMode(_modes.IndexOf(mode));
    }

    private void SelectMode(int index)
    {
        if (index < 0 || index >= _modes.Count) return;
        _idleTimer?.Stop();
        _matrix?.Stop();
        _particles?.Stop();
        _loadingWindow = new LoadingWindow(_modes[index], index) { Owner = this };
        _loadingWindow.Show();
        Hide();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ResetIdleTimer();

        int? modeIndex = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            _ => null
        };

        if (modeIndex.HasValue)
        {
            SelectMode(modeIndex.Value);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (FocusManager.GetFocusedElement(this) is Button btn)
                ModeButton_Click(btn, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == RootGrid)
            CloseWithFade();
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
