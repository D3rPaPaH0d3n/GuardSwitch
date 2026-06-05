using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WgAutoswitch.Shared;

namespace WgAutoswitch.LinuxTray;

public partial class MainWindow : Window
{
    private const string AppName = "GuardSwitch";

    private readonly PipeClient _client = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly TrayIcon _trayIcon = new();
    private StatusMessage? _last;

    public MainWindow()
    {
        InitializeComponent();

        RefreshButton.Click += async (_, _) => await RefreshAsync();
        PauseButton.Click += async (_, _) => await TogglePauseAsync();
        ReloadButton.Click += async (_, _) => await ReloadConfigAsync();

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        BuildTray();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        _ = RefreshAsync();
    }

    private void BuildTray()
    {
        _trayIcon.ToolTipText = AppName;
        _trayIcon.Menu = new NativeMenu
        {
            new NativeMenuItem("Status wird geladen...") { IsEnabled = false },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Anzeigen") { Command = new SimpleCommand(ShowWindow) },
            new NativeMenuItem("Auto-Modus pausieren") { Command = new SimpleCommand(async () => await TogglePauseAsync()) },
            new NativeMenuItem("Config neu laden") { Command = new SimpleCommand(async () => await ReloadConfigAsync()) },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Beenden") { Command = new SimpleCommand(Shutdown) }
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();

        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, new TrayIcons { _trayIcon });
    }

    private async Task RefreshAsync()
    {
        var resp = await _client.SendAsync(new GetStatusCommand(), CancellationToken.None);
        if (!resp.Success || resp.Status == null)
        {
            _last = null;
            StatusText.Text = resp.Error ?? "Service nicht erreichbar";
            PauseButton.IsEnabled = false;
            TunnelPanel.Children.Clear();
            _trayIcon.ToolTipText = $"{AppName}: Service nicht erreichbar";
            UpdateTrayMenu(null);
            return;
        }

        _last = resp.Status;
        PauseButton.IsEnabled = true;
        PauseButton.Content = resp.Status.AutoModeEnabled ? "Auto pausieren" : "Auto aktivieren";

        var status = resp.Status.AutoModeEnabled
            ? (resp.Status.AtHome ? "Zuhause - Tunnel aus" : "Unterwegs - Tunnel an")
            : "Pausiert";
        StatusText.Text = $"{status}\n{resp.Status.LastDetectionReason}";
        _trayIcon.ToolTipText = $"{AppName}: {status}";

        UpdateTunnelPanel(resp.Status);
        UpdateTrayMenu(resp.Status);
    }

    private void UpdateTunnelPanel(StatusMessage status)
    {
        TunnelPanel.Children.Clear();
        foreach (var (name, tunnel) in status.Tunnels)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
            };
            row.Children.Add(new TextBlock
            {
                Text = $"{name}: {tunnel.ServiceState}",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            var on = new Button { Content = "Ein", Margin = new Thickness(0, 0, 8, 0) };
            on.Click += async (_, _) => await ManualTunnelAsync(name, true);
            Grid.SetColumn(on, 1);
            row.Children.Add(on);

            var off = new Button { Content = "Aus" };
            off.Click += async (_, _) => await ManualTunnelAsync(name, false);
            Grid.SetColumn(off, 2);
            row.Children.Add(off);

            TunnelPanel.Children.Add(row);
        }
    }

    private void UpdateTrayMenu(StatusMessage? status)
    {
        if (_trayIcon.Menu == null) return;

        _trayIcon.Menu.Items.Clear();
        var label = status == null
            ? "Service nicht erreichbar"
            : status.AutoModeEnabled
                ? (status.AtHome ? "Zuhause - Tunnel aus" : "Unterwegs - Tunnel an")
                : "Pausiert";

        _trayIcon.Menu.Items.Add(new NativeMenuItem(label) { IsEnabled = false });
        _trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Items.Add(new NativeMenuItem("Anzeigen") { Command = new SimpleCommand(ShowWindow) });

        if (status != null)
        {
            _trayIcon.Menu.Items.Add(new NativeMenuItem(status.AutoModeEnabled ? "Auto-Modus pausieren" : "Auto-Modus aktivieren")
            {
                Command = new SimpleCommand(async () => await TogglePauseAsync())
            });

            foreach (var (name, tunnel) in status.Tunnels)
            {
                var tunnelMenu = new NativeMenuItem($"Tunnel \"{name}\": {tunnel.ServiceState}")
                {
                    Menu = new NativeMenu
                    {
                        new NativeMenuItem("Manuell EIN") { Command = new SimpleCommand(async () => await ManualTunnelAsync(name, true)) },
                        new NativeMenuItem("Manuell AUS") { Command = new SimpleCommand(async () => await ManualTunnelAsync(name, false)) }
                    }
                };
                _trayIcon.Menu.Items.Add(tunnelMenu);
            }

            _trayIcon.Menu.Items.Add(new NativeMenuItem("Config neu laden") { Command = new SimpleCommand(async () => await ReloadConfigAsync()) });
        }

        _trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Items.Add(new NativeMenuItem("Beenden") { Command = new SimpleCommand(Shutdown) });
    }

    private async Task TogglePauseAsync()
    {
        if (_last == null) return;
        var newState = !_last.AutoModeEnabled;
        await _client.SendAsync(new SetAutoModeCommand(newState), CancellationToken.None);
        await RefreshAsync();
    }

    private async Task ReloadConfigAsync()
    {
        await _client.SendAsync(new ReloadConfigCommand(), CancellationToken.None);
        await RefreshAsync();
    }

    private async Task ManualTunnelAsync(string name, bool active)
    {
        await _client.SendAsync(new ManualTunnelCommand(name, active), CancellationToken.None);
        await RefreshAsync();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private sealed class SimpleCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public SimpleCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
