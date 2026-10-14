// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Win32;
using VictusHub.Core.Hardware;
using VictusHub.Core.Telemetry;
using VictusHub.Core.Control;
using Forms = System.Windows.Forms;

namespace VictusHub.App;
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly WindowsTelemetry _windows = new();
    private NvidiaTelemetry? _nvidia;
    private readonly Forms.NotifyIcon _tray;
    private bool _sampling, _exit, _refreshingControl;
    private bool _readingSensors;
    private SensorReading? _serviceSensors;
    private string? _sensorError;
    private bool _connected, _controlling;
    private ControlStatus? _controlStatus;
    private readonly DesktopEcoManager _desktopEco = new(SettingsDirectory);
    private DateTime _nextControlRead = DateTime.MinValue;
    private UserPreferences _preferences;
    private static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictusHub");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private readonly bool _previewOnly = Array.IndexOf(App.Arguments, "--design-preview") >= 0;

    public MainWindow()
    {
        InitializeComponent();
        Height = Math.Min(410, SystemParameters.WorkArea.Height - 24);
        _preferences = UserPreferences.Load(SettingsDirectory);
        CloseToTrayCheck.IsChecked = _preferences.CloseToTray;
        StartupCheck.IsChecked = UserStartup.IsEnabled;
        if (!_previewOnly && StartupCheck.IsChecked == true) UserStartup.SetEnabled(true);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Victus Gaming Hub", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        var profiles = new Forms.ToolStripMenuItem("Performance");
        foreach (var profile in Enum.GetValues<DeviceProfile>())
            profiles.DropDownItems.Add(profile.ToString(), null, (_, _) => Dispatcher.Invoke(async () => await ApplyProfile(profile)));
        menu.Items.Add(profiles);
        menu.Items.Add("Fan: Auto", null, (_, _) => Dispatcher.Invoke(async () => await SetFan(FanChoice.Auto)));
        menu.Items.Add("Fan: Max", null, (_, _) => Dispatcher.Invoke(async () => await SetFan(FanChoice.Max)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Exit));
        _tray = new Forms.NotifyIcon { Text = "Victus Gaming Hub", Icon = LoadTrayIcon(), ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        _timer.Tick += async (_, _) => await RefreshTelemetry();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _timer.Start(); _ = RefreshTelemetry(); }
            else { _timer.Stop(); _ = PauseServiceSensors(); }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                _timer.Stop();
                _ = PauseServiceSensors();
                if (!_sampling) { _nvidia?.Dispose(); _nvidia = null; }
            }
            else if (IsVisible) { _timer.Start(); _ = RefreshTelemetry(); }
        };
        if (!_previewOnly) _ = InitializeControl();
        Loaded += async (_, _) =>
        {
            var machine = await Task.Run(MachineIdentity.Detect);
            DeviceName.Text = machine.Model;
            DeviceDetail.Text = $"{machine.Cpu} · {machine.Board} · BIOS {machine.Bios}";
            if (_previewOnly)
            {
                try
                {
                    RenderControl(await HubProtocol.Send(new(HubAction.Status)));
                    _serviceSensors = (await HubProtocol.Send(new(HubAction.Telemetry))).Sensors;
                }
                catch (Exception error) { SettingsToggle.ToolTip = error.Message; }
                await RefreshTelemetry();
                await Task.Delay(2300);
                await RefreshTelemetry();
                if (Array.IndexOf(App.Arguments, "--temperature-colors") >= 0)
                {
                    ShowTemperature(CpuTemperature, 80);
                    ShowTemperature(GpuTemperature, 90);
                }
                SavePreview(App.Arguments[Array.IndexOf(App.Arguments, "--design-preview") + 1]);
                _exit = true;
                _tray.Visible = false;
                _tray.Icon?.Dispose();
                _tray.Dispose();
                _nvidia?.Dispose();
                System.Windows.Application.Current.Shutdown();
                return;
            }
            await RefreshTelemetry();
            int smoke = Array.IndexOf(App.Arguments, "--smoke-test");
            if (smoke >= 0 && smoke + 1 < App.Arguments.Length)
            {
                await RunSmokeTest(App.Arguments[smoke + 1]);
                return;
            }
            int preview = Array.IndexOf(App.Arguments, "--render-preview");
            if (preview >= 0 && preview + 1 < App.Arguments.Length)
            {
                await Task.Delay(2500);
                await RefreshTelemetry();
                SavePreview(App.Arguments[preview + 1]);
                Exit();
            }
        };
        Closing += OnClosing;
    }

    private async Task RefreshTelemetry()
    {
        if (_sampling || !IsVisible || WindowState == WindowState.Minimized) return;
        _sampling = true;
        try
        {
            var sample = await Task.Run(() =>
            {
                var sensors = _serviceSensors;
                if (sensors is not null && DateTimeOffset.Now - sensors.At < TimeSpan.FromSeconds(8))
                    return (Memory: _windows.ReadMemory(), Cpu: _windows.ReadCpuUsage(), Gpu: sensors.Nvidia ?? new GpuReading("NVIDIA GPU", null, null, null, "NVIDIA sensors unavailable."));
                _nvidia ??= new NvidiaTelemetry();
                return (Memory: _windows.ReadMemory(), Cpu: _windows.ReadCpuUsage(), Gpu: _nvidia.Read());
            });
            RamValue.Text = Format(sample.Memory.Percent, "0") + "%";
            RamDetail.Text = $"{Format(sample.Memory.UsedGiB, "0.0")} / {Format(sample.Memory.TotalGiB, "0.0")} GiB";
            CpuValue.Text = Metric(sample.Cpu, "0", "%");
            var currentSensors = _serviceSensors;
            bool fresh = currentSensors is not null && DateTimeOffset.Now - currentSensors.At < TimeSpan.FromSeconds(8);
            ShowTemperature(CpuTemperature, fresh ? currentSensors!.Cpu.Temperature : null);
            CpuPower.Text = Metric(fresh ? currentSensors!.Cpu.PowerWatts : null, "0.0", " W");
            CpuTemperature.ToolTip = _sensorError;
            GpuValue.Text = Metric(sample.Gpu.Usage, "0", "%");
            ShowTemperature(GpuTemperature, sample.Gpu.Temperature);
            GpuPower.Text = Metric(sample.Gpu.PowerWatts, "0.0", " W");
            GpuTemperature.ToolTip = sample.Gpu.Error;
            if (_connected) _ = RefreshServiceSensors();
            PowerState.Text = Forms.SystemInformation.PowerStatus.PowerLineStatus == Forms.PowerLineStatus.Online ? "AC power" : "Battery";
            if (!_controlling && _connected && DateTime.UtcNow >= _nextControlRead)
            {
                _nextControlRead = DateTime.UtcNow.AddSeconds(10);
                _ = RefreshControlInBackground();
            }
        }
        catch (Exception e) { SettingsToggle.ToolTip = "Sensor error: " + e.Message; }
        finally
        {
            if (!IsVisible || WindowState == WindowState.Minimized) { _nvidia?.Dispose(); _nvidia = null; }
            _sampling = false;
        }
    }
    private static string Format(double value, string format) => value.ToString(format, English);
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Victus.ico")).Stream;
        using var icon = new System.Drawing.Icon(resource, new System.Drawing.Size(32, 32));
        return (System.Drawing.Icon)icon.Clone();
    }
    private static void ShowTemperature(TextBlock target, double? temperature)
    {
        target.Text = Metric(temperature, "0", " °C");
        target.Foreground = temperature switch
        {
            >= 90 => Brushes.Tomato,
            >= 80 => Brushes.Gold,
            _ => Brushes.WhiteSmoke
        };
    }
    private async Task RefreshServiceSensors()
    {
        if (_readingSensors || _exit || !IsVisible || WindowState == WindowState.Minimized) return;
        _readingSensors = true;
        try
        {
            var response = await HubProtocol.Send(new(HubAction.Telemetry));
            _serviceSensors = response.Success ? response.Sensors : null;
            _sensorError = response.Success ? null : response.Message;
        }
        catch (Exception error) { _serviceSensors = null; _sensorError = "Sensor connection: " + error.Message; }
        finally { _readingSensors = false; }
    }
    private async Task PauseServiceSensors()
    {
        _serviceSensors = null;
        if (!_connected || _exit) return;
        try { await HubProtocol.Send(new(HubAction.PauseTelemetry)); }
        catch { /* The service owns no polling loop; a lost pause request cannot keep sampling. */ }
    }
    private async Task RefreshControlInBackground()
    {
        if (_refreshingControl) return;
        _refreshingControl = true;
        try
        {
            var status = await HubProtocol.Send(new(HubAction.Status));
            if (!_controlling) { _controlStatus = status; RenderControl(status); }
        }
        catch (Exception error) { SettingsToggle.ToolTip = "Control unavailable: " + error.Message; }
        finally { _refreshingControl = false; }
    }
    private static string Metric(double? value, string format, string unit) => value.HasValue ? Format(value.Value, format) + unit : "—";
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exit) return;
        e.Cancel = true;
        if (!_exit && _preferences.CloseToTray)
        {
            Hide();
            if (!_sampling) { _nvidia?.Dispose(); _nvidia = null; }
        }
        else Exit();
    }
    private async void Exit()
    {
        if (_exit) return;
        _exit = true;
        _timer.Stop();
        IsEnabled = false;
        try
        {
            _desktopEco.Restore();
            if (_connected) await HubProtocol.Send(new(HubAction.Release));
            await Task.Run(ServiceAccess.Stop);
        }
        catch (Exception error) { System.Windows.MessageBox.Show("Could not restore settings: " + error.Message, "Victus Gaming Hub"); }
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.Dispose();
        if (!_sampling) _nvidia?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
    public void RequestExit() => Exit();
    private async void Profile_Click(object sender, RoutedEventArgs e) => await ApplyProfile(Enum.Parse<DeviceProfile>((string)((Button)sender).Tag));
    private async void Fan_Click(object sender, RoutedEventArgs e) => await SetFan(Enum.Parse<FanChoice>((string)((Button)sender).Tag));
    private async Task InitializeControl()
    {
        try
        {
            _desktopEco.Restore();
            await Task.Run(ServiceAccess.EnsureRunning);
            for (int retry = 0; retry < 4; retry++)
            {
                try { await RefreshControl(); return; }
                catch (System.TimeoutException) when (retry < 3) { await Task.Delay(500); }
            }
        }
        catch (Exception error)
        {
            SettingsToggle.ToolTip = "Control offline. Run the installer to enable control.";
            ProfileDetail.Text = "Run the installer to enable control: " + error.Message;
            UpdateControlButtons();
        }
    }
    private async Task RefreshControl()
    {
        _controlStatus = await HubProtocol.Send(new(HubAction.Status));
        _connected = true;
        RenderControl(_controlStatus);
    }
    private void RenderControl(ControlStatus status)
    {
        _controlStatus = status;
        ProfileDetail.Text = status.LastRequestedProfile is { } profile
            ? $"Last applied: {profile}"
            : "Select a profile";
        FanDetail.Text = status.ConfirmedFan is { } fan ? $"{fan}" : "Unavailable";
        foreach (var button in new[] { EcoButton, QuietButton, DefaultButton, PerformanceButton })
        {
            bool selected = (string)button.Tag == status.LastRequestedProfile?.ToString();
            button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(168, 180, 255) : Color.FromRgb(48, 51, 64));
            button.Background = new SolidColorBrush(selected ? Color.FromRgb(38, 44, 71) : Color.FromRgb(27, 29, 38));
        }
        foreach (var button in new[] { AutoButton, MaxButton })
        {
            bool selected = (string)button.Tag == status.ConfirmedFan?.ToString();
            button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(168, 180, 255) : Color.FromRgb(48, 51, 64));
            button.Background = new SolidColorBrush(selected ? Color.FromRgb(38, 44, 71) : Color.FromRgb(27, 29, 38));
        }
        UpdateControlButtons();
    }
    private void UpdateControlButtons()
    {
        bool available = (_connected || _previewOnly) && _controlStatus is { Success: true } && !_controlling;
        foreach (var button in new[] { EcoButton, QuietButton, DefaultButton, PerformanceButton, AutoButton }) button.IsEnabled = available;
        MaxButton.IsEnabled = available && _controlStatus?.LastRequestedProfile != DeviceProfile.Eco;
    }
    private async Task ApplyProfile(DeviceProfile profile)
    {
        if (_previewOnly || !_connected || _controlling) return;
        _controlling = true;
        UpdateControlButtons();
        try
        {
            if (profile == DeviceProfile.Eco) _desktopEco.Enter();
            var result = await HubProtocol.Send(new(HubAction.ApplyProfile, profile));
            if (profile != DeviceProfile.Eco || !result.Success) _desktopEco.Restore();
            RenderControl(result);
            if (!result.Success) SettingsToggle.ToolTip = "Could not apply profile: " + result.Message;
        }
        catch (Exception error)
        {
            try { _desktopEco.Restore(); } catch { }
            SettingsToggle.ToolTip = "Profile failed: " + error.Message;
        }
        finally { _controlling = false; UpdateControlButtons(); }
    }
    private async Task SetFan(FanChoice fan)
    {
        if (_previewOnly || !_connected || _controlling) return;
        _controlling = true;
        UpdateControlButtons();
        try { var result = await HubProtocol.Send(new(HubAction.SetFan, Fan: fan)); RenderControl(result); if (!result.Success) SettingsToggle.ToolTip = "Could not set fan: " + result.Message; }
        catch (Exception error) { SettingsToggle.ToolTip = error.Message; }
        finally { _controlling = false; UpdateControlButtons(); }
    }
    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        _preferences = _preferences with { CloseToTray = CloseToTrayCheck.IsChecked == true };
        _preferences.Save(SettingsDirectory);
    }
    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UserStartup.SetEnabled(StartupCheck.IsChecked == true);
        }
        catch (Exception error) { StartupCheck.IsChecked = !StartupCheck.IsChecked; SettingsToggle.ToolTip = error.Message; }
    }
    private void SavePreview(string path)
    {
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var target = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // Explicit development QA entry point. Exercises the same handlers as the visible controls.
    private async Task RunSmokeTest(string directory)
    {
        Directory.CreateDirectory(directory);
        string report = Path.Combine(directory, "ui-smoke.json");
        _timer.Stop();
        try
        {
            for (int retry = 0; !_connected && retry < 40; retry++) await Task.Delay(250);
            if (!_connected || _controlStatus is not { Success: true }) throw new InvalidOperationException("Control service is not ready.");
            var beforeDisplay = DisplayRefresh.Read();
            FrameLimitSetting beforeLimit;
            using (var limiter = new NvidiaFrameLimiter()) beforeLimit = limiter.Read();
            await ApplyProfile(DeviceProfile.Eco);
            var ecoDisplay = DisplayRefresh.Read();
            FrameLimitSetting ecoLimit;
            using (var limiter = new NvidiaFrameLimiter()) ecoLimit = limiter.Read();
            if (_controlStatus is not { Success: true, LastRequestedProfile: DeviceProfile.Eco, ConfirmedFan: FanChoice.Auto }) throw new InvalidOperationException("Eco kontrolü doğrulanamadı.");
            if (ecoDisplay.Frequency != Math.Min(beforeDisplay.Frequency, 60) || ecoLimit.FramesPerSecond != NvidiaFrameLimiter.EcoLimit(beforeLimit.FramesPerSecond)) throw new InvalidOperationException("Eco ekran/FPS politikası eşleşmedi.");
            await RefreshTelemetry();
            await Task.Delay(500);
            SavePreview(Path.Combine(directory, "ui-eco.png"));
            await ApplyProfile(DeviceProfile.Default);
            var afterDisplay = DisplayRefresh.Read();
            FrameLimitSetting afterLimit;
            using (var limiter = new NvidiaFrameLimiter()) afterLimit = limiter.Read();
            if (_controlStatus is not { Success: true, LastRequestedProfile: DeviceProfile.Default, ConfirmedFan: FanChoice.Auto } || afterDisplay.Frequency != beforeDisplay.Frequency || afterLimit != beforeLimit)
                throw new InvalidOperationException("Default/Auto veya ekran/FPS geri yüklemesi eşleşmedi.");
            await RefreshTelemetry();
            SavePreview(Path.Combine(directory, "ui-default.png"));
            File.WriteAllText(report, JsonSerializer.Serialize(new { Success = true, At = DateTimeOffset.Now, Before = new { Display = beforeDisplay, FrameLimit = beforeLimit }, Eco = new { Display = ecoDisplay, FrameLimit = ecoLimit }, After = new { Display = afterDisplay, FrameLimit = afterLimit }, Control = _controlStatus }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { Success = false, Error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
        finally
        {
            if (_connected) await ApplyProfile(DeviceProfile.Default);
            try { _desktopEco.Restore(); } catch { }
            Exit();
        }
    }
}

internal sealed record UserPreferences(bool CloseToTray = true)
{
    public static UserPreferences Load(string directory)
    {
        try { return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Path.Combine(directory, "settings.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}



