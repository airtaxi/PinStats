using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.Resources;
using PinStats.Services;
using PinStats.ViewModels;
using WinUIEx;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;

namespace PinStats;

public sealed partial class MonitorWindow : IDisposable
{
    private const int RefreshTimerIntervalInMilliseconds = 1000;

    public static MonitorWindow Instance { get; set; }

    private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
    private readonly UsageViewModel _cpuUsageViewModel;
    private readonly UsageViewModel _gpuUsageViewModel;
    private readonly Monitor _monitor;
    private readonly Timer _refreshTimer;
    private bool _disposed;
    private string _latestCpuBaseText = string.Empty;
    private string _latestGpuBaseText = string.Empty;
    private Arm64PowerMeterValues _cachedArm64PowerMeterValues;
    private int _powerFetchInProgress;

    public MonitorWindow(Monitor monitor)
    {
        Instance = this;
        _monitor = monitor;
        _cpuUsageViewModel = new(UsageHistoryMetric.CpuUsage, _localizationService);
        _gpuUsageViewModel = new(UsageHistoryMetric.GpuUsage, _localizationService);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;

        LoadUsageHistory();
        UsageHistoryBuffer.UsageInformationAdded += OnUsageInformationAdded;

        InitializeControls();
        RefreshHardwareInformation();

        // Setup the timer to refresh the hardware information
        _refreshTimer = new(RefreshTimerCallback, null, RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)

        // WinUI bug workaround: Indicate this windows is full screen
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    private void InitializeControls()
    {
        // Apply background image if available or use Mica backdrop
        if (!BackgroundImageHelper.TrySetupBackgroundImage(BackgroundImageType.HardwareMonitor, ImageBackground))
        {
            GridBackground.Visibility = Visibility.Collapsed;
            SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
        // No need to set the backdrop to null since it is already null by default

        TaskbarUsageResource.HardwareMonitorBackgroundImageSet += OnHardwareMonitorBackgroundImageSet;

        TextBlockMotherboardName.Text = HardwareMonitor.GetMotherboardName();
        TextBlockCpuName.Text = HardwareMonitor.GetCpuName();
        TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();

        CartesianChartMemory.Series = MemoryUsageViewModel.Series;
        CartesianChartMemory.XAxes = MemoryUsageViewModel.XAxes;
        CartesianChartMemory.YAxes = MemoryUsageViewModel.YAxes;
        CartesianChartVirtualMemory.DataContext = VirtualMemoryUsageViewModel;

        // Setup CPU usage chart
        CartesianChartCpuUsage.Series = _cpuUsageViewModel.Series;
        CartesianChartCpuUsage.XAxes = _cpuUsageViewModel.XAxes;
        CartesianChartCpuUsage.YAxes = _cpuUsageViewModel.YAxes;

        // Setup GPU usage chart
        CartesianChartGpuUsage.Series = _gpuUsageViewModel.Series;
        CartesianChartGpuUsage.XAxes = _gpuUsageViewModel.XAxes;
        CartesianChartGpuUsage.YAxes = _gpuUsageViewModel.YAxes;

        PopupWindow.OnCurrentGpuChanged += OnCurrentGpuChanged;

        CartesianChartBattery.DataContext = BatteryViewModel;
        CartesianChartBatteryHealth.DataContext = BatteryHealthViewModel;

        BatteryViewModel.SetValue(100, 0, _localizationService.GetLocalizedString("Info.NotAvailable"));
        BatteryHealthViewModel.SetValue(100, 0, _localizationService.GetLocalizedString("Info.NotAvailable"));
    }

    private void LoadUsageHistory()
    {
        var usageInformationHistory = UsageHistoryBuffer.GetSnapshot();
        _cpuUsageViewModel.LoadUsageInformation(usageInformationHistory);
        _gpuUsageViewModel.LoadUsageInformation(usageInformationHistory);
    }

    private void OnUsageInformationAdded(object sender, UsageInformation usageInformation)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;

            _cpuUsageViewModel.AddUsageInformation(usageInformation);
            _gpuUsageViewModel.AddUsageInformation(usageInformation);
        });
    }

    private void RefreshTimerCallback(object state)
    {
        try { RefreshHardwareInformation(); }
        catch { } // Ignore. Hardware is unpredictable.
        finally { RestartRefreshTimer(); }
    }

    private void RefreshHardwareInformation()
    {
        if (_disposed) return;

        HardwareMonitor.UpdateCpuHardware();
        HardwareMonitor.UpdateMemoryHardware();
        HardwareMonitor.UpdateNetworkHardware();
        HardwareMonitor.UpdateStorageHardware();
        HardwareMonitor.UpdateCurrentGpuHardware();
        if (HardwareMonitor.HasBattery()) HardwareMonitor.UpdateBatteryHardware();

        // CPU
        var isArm64 = HardwareMonitor.IsArm64Architecture;
        var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
        var cpuBaseText = $"{cpuUsage:N0}%";
        if (!isArm64)
        {
            var cpuTemperature = HardwareMonitor.GetAverageCpuTemperature();
            var cpuPower = HardwareMonitor.GetTotalCpuPackagePower();
            var cpuTemperatureText = cpuTemperature != null ? (" / " + cpuTemperature.Value.ToString("N0") + "°C") : "";
            var cpuPowerText = cpuPower != 0 ? (" / " + cpuPower.ToString("N0") + " W") : "";
            cpuBaseText += cpuTemperatureText + cpuPowerText;
        }

        // GPU
        var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();
        var gpuBaseText = $"{gpuUsage:N0}%";
        if (!isArm64)
        {
            var gpuTemperature = HardwareMonitor.GetCurrentGpuTemperature();
            var gpuPower = HardwareMonitor.GetCurrentGpuPower();
            var gpuTemperatureText = gpuTemperature != null ? (" / " + gpuTemperature.Value.ToString("N0") + "°C") : "";
            var gpuPowerText = gpuPower != 0 ? (" / " + gpuPower.ToString("N0") + " W") : "";
            gpuBaseText += gpuTemperatureText + gpuPowerText;
        }

        // On ARM64, append the cached power meter text so the UI does not flicker while the next power reading is fetched asynchronously.
        var cpuInformationText = cpuBaseText;
        var gpuInformationText = gpuBaseText;
        if (isArm64)
        {
            var cachedPowerMeterValues = _cachedArm64PowerMeterValues;
            cpuInformationText += cachedPowerMeterValues.GetCpuPowerInformationText();
            gpuInformationText += cachedPowerMeterValues.GetGpuPowerInformationText();
        }

        _latestCpuBaseText = cpuBaseText;
        _latestGpuBaseText = gpuBaseText;

        // Memory
        var totalMemory = HardwareMonitor.GetTotalMemory();
        var usedMemory = HardwareMonitor.GetUsedMemory();
        var memoryInformationText = _localizationService.GetFormattedString("Info.MemoryUsageFormat", $"{usedMemory:N2}", $"{totalMemory:N2}");

        // Virtual Memory
        var totalVirtualMemory = HardwareMonitor.GetTotalMemory(true);
        var usedVirtualMemory = HardwareMonitor.GetUsedMemory(true);
        var virtualMemoryInformationText = _localizationService.GetFormattedString("Info.MemoryUsageFormat", $"{usedVirtualMemory:N2}", $"{totalVirtualMemory:N2}");

        // Network
        var networkUploadSpeed = (float)HardwareMonitor.GetNetworkTotalUploadSpeedInBytes() / 1024;
        var networkDownloadSpeed = (float)HardwareMonitor.GetNetworkTotalDownloadSpeedInBytes() / 1024;
        var networkInformationText = _localizationService.GetFormattedString("Info.NetworkFormat", $"{networkUploadSpeed:N0}", $"{networkDownloadSpeed:N0}");

        // Storage
        var storageReadRate = HardwareMonitor.GetStorageReadRatePerSecondInBytes() / 1024;
        var storageWriteRate = HardwareMonitor.GetStorageWriteRatePerSecondInBytes() / 1024;
        var storageInformationText = _localizationService.GetFormattedString("Info.StorageFormat", $"{storageReadRate:N0}", $"{storageWriteRate:N0}");

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;

            TextBlockCpuInformation.Text = cpuInformationText;
            TextBlockGpuInformation.Text = gpuInformationText;
            MemoryUsageViewModel.SetValue(totalMemory, usedMemory, memoryInformationText);
            VirtualMemoryUsageViewModel.SetValue(totalVirtualMemory, usedVirtualMemory, virtualMemoryInformationText);

            // If the device has a battery, update the battery information.
            if (HardwareMonitor.HasBattery())
            {
                var batteryPercentage = HardwareMonitor.GetTotalBatteryPercent().Value; // Assume that the device has a battery due to the if statement above.
                var batteryChargeRate = HardwareMonitor.GetTotalBatteryChargeRate();
                var batteryEstimatedTime = HardwareMonitor.GetTotalBatteryEstimatedTime();

                // Battery information
                var batteryChargeRateText = string.Empty;
                if (batteryChargeRate.HasValue)
                {
                    var batteryChargeRatePrefix = string.Empty;
                    if (batteryChargeRate.Value > 0) batteryChargeRatePrefix = "+";
                    batteryChargeRateText = " / " + batteryChargeRatePrefix + batteryChargeRate.Value.ToString("N1") + " W";
                }
                var batteryEstimatedTimeText = string.Empty;
                if (batteryEstimatedTime.HasValue) batteryEstimatedTimeText = _localizationService.GetFormattedString("Info.BatteryTimeLeft", batteryEstimatedTime.Value.ToString(@"hh\:mm\:ss"));

                var batteryViewModelDataLabelText = $"{batteryPercentage:N0}%" + batteryChargeRateText + batteryEstimatedTimeText;

                // Apply battery information to the view model
                BatteryViewModel.SetValue(100, batteryPercentage, batteryViewModelDataLabelText);

                // Battery health
                var batteryHealthPercent = HardwareMonitor.GetAverageBatteryHealthPercent();
                if (batteryHealthPercent.HasValue)
                {
                    var batteryHealthViewModelDataLabelText = $"{batteryHealthPercent:N0}%";
                    BatteryHealthViewModel.SetValue(100, batteryHealthPercent.Value, batteryHealthViewModelDataLabelText);
                }
            }

            TextBlockNetworkInformation.Text = networkInformationText;
            TextBlockStorageInformation.Text = storageInformationText;
        });

        if (isArm64 && Interlocked.Exchange(ref _powerFetchInProgress, 1) == 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (_disposed) return;
                    var powerMeterValues = HardwareMonitor.GetArm64PowerMeterValues();
                    _cachedArm64PowerMeterValues = powerMeterValues;
                    var cpuPowerText = powerMeterValues.GetCpuPowerInformationText();
                    var gpuPowerText = powerMeterValues.GetGpuPowerInformationText();
                    var latestCpuBaseText = _latestCpuBaseText;
                    var latestGpuBaseText = _latestGpuBaseText;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_disposed) return;
                        TextBlockCpuInformation.Text = latestCpuBaseText + cpuPowerText;
                        TextBlockGpuInformation.Text = latestGpuBaseText + gpuPowerText;
                    });
                }
                catch { } // Ignore. Hardware is unpredictable.
                finally { Interlocked.Exchange(ref _powerFetchInProgress, 0); }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Instance == this) Instance = null;
        TaskbarUsageResource.HardwareMonitorBackgroundImageSet -= OnHardwareMonitorBackgroundImageSet;
        PopupWindow.OnCurrentGpuChanged -= OnCurrentGpuChanged;
        UsageHistoryBuffer.UsageInformationAdded -= OnUsageInformationAdded;
        StopRefreshTimer();
        GC.SuppressFinalize(this);
    }

    private void RestartRefreshTimer()
    {
        if (_disposed) return;

        try { _refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private void StopRefreshTimer()
    {
        try { _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException) { }

        _refreshTimer.Dispose();
    }

    private void OnHardwareMonitorBackgroundImageSet(object sender, EventArgs e)
    {
        if (_disposed) return;

        // Apply background image if available or use Mica backdrop
        if (!BackgroundImageHelper.TrySetupBackgroundImage(BackgroundImageType.HardwareMonitor, ImageBackground))
        {
            GridBackground.Visibility = Visibility.Collapsed;
            SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        }
        else
        {
            GridBackground.Visibility = Visibility.Visible;
            SystemBackdrop = null; // Reset backdrop to default since the background image is available
        }
    }

    private void OnCurrentGpuChanged(object sender, EventArgs e)
    {
        if (_disposed) return;
        TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();
    }

    private void OnExitButtonClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object sender, WindowEventArgs args) => Dispose();

    // Position and setup presenter should be done after the window is loaded (probably issue with WinUI 3)
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Task.Delay(100);
        if (_disposed) return;

        MonitorHelper.PositionWindowToMonitor(this.GetWindowHandle(), _monitor);
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();
}
