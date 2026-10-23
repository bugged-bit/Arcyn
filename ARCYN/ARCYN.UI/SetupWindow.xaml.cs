using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ARCYN.UI.Models;
using ARCYN.UI.Services;


namespace ARCYN.UI;

public partial class SetupWindow : Window
{
    private readonly ConfigService _config;
    private readonly List<ModeConfig> _modes = [];
    private ModeConfig? _currentMode;
    private int _step;
    private bool _finishing;
    private static readonly string[] StepTitles = ["Welcome", "Workspace", "Apps", "Folders", "Websites", "Review"];

        public SetupWindow(ConfigService? service = null)
        {
            _config = service ?? new ConfigService();
            InitializeComponent();
            RefreshSystemTheme();
            WindowSurface.Prepare(this, new());
            _currentMode = NewMode();
            ShowStep(0);
        }

        internal void RefreshSystemTheme() => ThemeService.Apply(this, new());

    private static ModeConfig NewMode()
    {
        return new ModeConfig
        {
            Name = "",
            Description = "",
            Accent = "#D64545"
        };
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepWelcome.Visibility = Visibility.Collapsed;
        StepMode.Visibility = Visibility.Collapsed;
        StepApps.Visibility = Visibility.Collapsed;
        StepFolders.Visibility = Visibility.Collapsed;
        StepWebsites.Visibility = Visibility.Collapsed;
        StepReview.Visibility = Visibility.Collapsed;

        switch (step)
        {
            case 0:
                StepWelcome.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Visible;
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Get started";
                StatusBar.Text = "Begin configuration";
                break;
            case 1:
                StepMode.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Visible;
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Next";
                StatusBar.Text = "Name your workspace";
                break;
            case 2:
                StepApps.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Visible;
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Next";
                StatusBar.Text = "Add applications to launch";
                break;
            case 3:
                StepFolders.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Visible;
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Next";
                StatusBar.Text = "Add folders to open";
                break;
            case 4:
                StepWebsites.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Visible;
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Next";
                StatusBar.Text = "Add websites to open";
                break;
            case 5:
                StepReview.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Collapsed;
                FinishButton.Visibility = Visibility.Visible;
                StatusBar.Text = _modes.Count == 1 ? "1 workspace configured" : $"{_modes.Count} workspaces configured";
                RefreshReview();
                break;
        }

        StepCounter.Text = $"{step + 1} / {StepTitles.Length}";
        SkipButton.Visibility = step is 2 or 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        Keyboard.Focus(step switch {
            1 => ModeNameInput, 2 => AppQuickInput, 3 => FolderBrowseButton,
            4 => WebsiteInput, 5 => FinishButton, _ => NextButton
        });
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 0:
                // Welcome → Mode creation
                ShowStep(1);
                break;

            case 1:
                // Save mode name/desc/color → Apps
                if (!CommitCurrentMode()) return;
                ShowStep(2);
                break;

            case 2:
                // Save apps → Folders
                if (sender != SkipButton) AddAppQuick_Click(sender, e);
                SaveCurrentApps();
                ShowStep(3);
                break;

            case 3:
                // Save folders → Websites
                SaveCurrentFolders();
                ShowStep(4);
                break;

            case 4:
                // Save websites → Review
                if (sender != SkipButton && !TryAddWebsite()) return;
                SaveCurrentWebsites();
                FinalizeCurrentMode();
                ShowStep(5);
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= 1)
        {
            ShowStep(0);
            return;
        }

        // When going back from review, restore last mode for editing
        if (_step == 5 && _modes.Count > 0)
        {
            _currentMode = _modes[^1];
            _modes.RemoveAt(_modes.Count - 1);
            RestoreCurrentModeUI();
        }

        ShowStep(_step - 1);
    }

    private bool CommitCurrentMode()
    {
        var name = ModeNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusBar.Text = "Name is required";
            return false;
        }

        _currentMode ??= NewMode();
        _currentMode.Name = name;
        _currentMode.Description = ModeDescInput.Text.Trim();
        _currentMode.Accent = GetSelectedAccent();

        return true;
    }

    private void SaveCurrentApps()
    {
        if (_currentMode == null) return;
        _currentMode.Apps.Clear();
        foreach (var item in AppsList.Items)
            _currentMode.Apps.Add(item.ToString() ?? string.Empty);
    }

    private void SaveCurrentFolders()
    {
        if (_currentMode == null) return;
        _currentMode.Folders.Clear();
        foreach (var item in FoldersList.Items)
            _currentMode.Folders.Add(item.ToString() ?? string.Empty);
    }

    private void SaveCurrentWebsites()
    {
        if (_currentMode == null) return;
        _currentMode.Websites.Clear();
        foreach (var item in WebsitesList.Items)
            _currentMode.Websites.Add(item.ToString() ?? string.Empty);
    }

    private void FinalizeCurrentMode()
    {
        if (_currentMode == null) return;
        _currentMode.Index = _modes.Count + 1;
        if (!string.IsNullOrWhiteSpace(_currentMode.Name))
            _modes.Add(_currentMode);
        _currentMode = NewMode();
    }

    private void RestoreCurrentModeUI()
    {
        if (_currentMode == null) return;
        ModeNameInput.Text = _currentMode.Name;
        ModeDescInput.Text = _currentMode.Description;
        SetAccentRadio(_currentMode.Accent);
        AppsList.Items.Clear();
        foreach (var a in _currentMode.Apps) AppsList.Items.Add(a);
        FoldersList.Items.Clear();
        foreach (var f in _currentMode.Folders) FoldersList.Items.Add(f);
        WebsitesList.Items.Clear();
        foreach (var w in _currentMode.Websites) WebsitesList.Items.Add(w);
    }

    private string GetSelectedAccent()
    {
        if (ColorRed.IsChecked == true) return "#D64545";
        if (ColorBlue.IsChecked == true) return "#4580D6";
        if (ColorGreen.IsChecked == true) return "#45D680";
        if (ColorOrange.IsChecked == true) return "#D6A045";
        if (ColorPurple.IsChecked == true) return "#A045D6";
        return "#D64545";
    }

    private void SetAccentRadio(string hex)
    {
        ColorRed.IsChecked = hex == "#D64545";
        ColorBlue.IsChecked = hex == "#4580D6";
        ColorGreen.IsChecked = hex == "#45D680";
        ColorOrange.IsChecked = hex == "#D6A045";
        ColorPurple.IsChecked = hex == "#A045D6";
    }

    private void RefreshReview()
    {
        // Show preview list using wrap objects with AccentBrush converted for binding
        var items = _modes.Select(m => new ModePreviewItem
        {
            Name = m.Name,
            Description = m.Description,
            ProcessCount = m.ProcessCount,
            AccentBrush = HexToBrush(m.Accent)
        }).ToList();
        ModesPreviewList.ItemsSource = items;
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an application",
            Filter = "Applications|*.exe;*.lnk;*.com;*.bat|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            AppsList.Items.Add(dialog.FileName);
    }

    private void AddAppQuick_Click(object sender, RoutedEventArgs e)
    {
        var text = AppQuickInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        AppsList.Items.Add(text);
        AppQuickInput.Clear();
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string app })
            AppsList.Items.Remove(app);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select a folder" };

        if (dialog.ShowDialog() == true)
        {
            var dir = dialog.FolderName;
            if (!string.IsNullOrEmpty(dir))
                FoldersList.Items.Add(dir);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder })
            FoldersList.Items.Remove(folder);
    }

    private void AddWebsite_Click(object sender, RoutedEventArgs e) => TryAddWebsite();

    private bool TryAddWebsite()
    {
        var text = WebsiteInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || string.IsNullOrEmpty(address.Host))
        {
            StatusBar.Text = "Enter a complete website address starting with http:// or https://";
            Keyboard.Focus(WebsiteInput);
            return false;
        }

        WebsitesList.Items.Add(text);
        WebsiteInput.Clear();
        return true;
    }

    private void RemoveWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string site })
            WebsitesList.Items.Remove(site);
    }

    private void AddMode_Click(object sender, RoutedEventArgs e)
    {
        // Clear inputs for new mode
        _currentMode = NewMode();
        ModeNameInput.Clear();
        ModeDescInput.Clear();
        AppsList.Items.Clear();
        FoldersList.Items.Clear();
        WebsitesList.Items.Clear();
        ColorBlue.IsChecked = true;

        ShowStep(1);
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_finishing) return;
        if (_modes.Count == 0)
        {
            StatusBar.Text = "Create at least one workspace to continue";
            return;
        }

        var config = new ArcynConfig
        {
            Modes = _modes
        };

        for (int i = 0; i < config.Modes.Count; i++)
            config.Modes[i].Index = i + 1;

        _finishing = true;
        IsEnabled = false;
        try { await _config.SaveAsync(config); }
        catch (Exception ex) { StatusBar.Text = "Save failed: " + ex.Message; _finishing = false; IsEnabled = true; return; }

        StatusBar.Text = "Config saved. Launching ARCYN...";

        await Task.Delay(300);
        if (!IsLoaded) return;


        var mainWindow = new MainWindow(_config, config);
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();

        Close();
    }

    private sealed class ModePreviewItem
    {
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Summary => $"{ProcessCount} targets";
        public int ProcessCount { get; init; }
        public SolidColorBrush AccentBrush { get; init; } = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
    }
}
