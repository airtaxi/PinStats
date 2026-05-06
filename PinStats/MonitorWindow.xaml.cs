using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.Resources;
using PinStats.ViewModels;
using WinUIEx;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;

namespace PinStats;

public sealed partial class MonitorWindow : IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 1000;

	public static MonitorWindow Instance { get; set; }

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public readonly static UsageViewModel GpuUsageViewModel = new();

	private readonly Monitor _monitor;
	private readonly Timer _refreshTimer;
	private bool _disposed;

	public MonitorWindow(Monitor monitor)
	{
		Instance = this;
		_monitor = monitor;
		InitializeComponent();

		ExtendsContentIntoTitleBar = true;
		AppWindow.IsShownInSwitchers = false;

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
		CpuUsageViewModel.RefreshSync();
		CartesianChartCpuUsage.Series = CpuUsageViewModel.Series;
		CartesianChartCpuUsage.XAxes = CpuUsageViewModel.XAxes;
		CartesianChartCpuUsage.YAxes = CpuUsageViewModel.YAxes;
		CartesianChartCpuUsage.SyncContext = CpuUsageViewModel.Sync;

		// Setup GPU usage chart
		GpuUsageViewModel.RefreshSync();
		CartesianChartGpuUsage.Series = GpuUsageViewModel.Series;
		CartesianChartGpuUsage.XAxes = GpuUsageViewModel.XAxes;
		CartesianChartGpuUsage.YAxes = GpuUsageViewModel.YAxes;
		CartesianChartGpuUsage.SyncContext = GpuUsageViewModel.Sync;

		PopupWindow.OnCurrentGpuChanged += OnCurrentGpuChanged;

		CartesianChartBattery.DataContext = BatteryViewModel;
		CartesianChartBatteryHealth.DataContext = BatteryHealthViewModel;

		BatteryViewModel.SetValue(100, 0, "N/A");
		BatteryHealthViewModel.SetValue(100, 0, "N/A");
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
		var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
		var cpuTemperature = HardwareMonitor.GetAverageCpuTemperature();

		var cpuInformationText = $"{cpuUsage:N0}%";
		var cpuTemperatureText = cpuTemperature != null ? (" / " + cpuTemperature.Value.ToString("N0") + "°C") : "";
		var cpuPowerText = HardwareMonitor.GetTotalCpuPackagePower() != 0 ? (" / " + HardwareMonitor.GetTotalCpuPackagePower().ToString("N0") + " W") : "";
		cpuInformationText += cpuTemperatureText + cpuPowerText;

		// GPU
		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();
		var gpuTemperature = HardwareMonitor.GetCurrentGpuTemperature();
		var gpuPower = HardwareMonitor.GetCurrentGpuPower();

		var gpuInformationText = $"{gpuUsage:N0}%";
		var gpuTemperatureText = gpuTemperature != null ? (" / " + gpuTemperature.Value.ToString("N0") + "°C") : "";
		var gpuPowerText = gpuPower != 0 ? (" / " + gpuPower.ToString("N0") + " W") : "";
		gpuInformationText += gpuTemperatureText + gpuPowerText;

		// Memory
		var totalMemory = HardwareMonitor.GetTotalMemory();
		var usedMemory = HardwareMonitor.GetUsedMemory();
		var memoryInformationText = $"{usedMemory:N2} / {totalMemory:N2} GB";

		// Virtual Memory
		var totalVirtualMemory = HardwareMonitor.GetTotalMemory(true);
		var usedVirtualMemory = HardwareMonitor.GetUsedMemory(true);
		var virtualMemoryInformationText = $"{usedVirtualMemory:N2} / {totalVirtualMemory:N2} GB";

		// Network
		var networkUploadSpeed = (float)HardwareMonitor.GetNetworkTotalUploadSpeedInBytes() / 1024;
		var networkDownloadSpeed = (float)HardwareMonitor.GetNetworkTotalDownloadSpeedInBytes() / 1024;
		var networkInformationText = $"U: {networkUploadSpeed:N0} KB/s D: {networkDownloadSpeed:N0} KB/s";

		// Storage
		var storageReadRate = HardwareMonitor.GetStorageReadRatePerSecondInBytes() / 1024;
		var storageWriteRate = HardwareMonitor.GetStorageWriteRatePerSecondInBytes() / 1024;
		var storageInformationText = $"R: {storageReadRate:N0} KB/s W: {storageWriteRate:N0} KB/s";

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
				if (batteryEstimatedTime.HasValue) batteryEstimatedTimeText = " / " + batteryEstimatedTime.Value.ToString(@"hh\:mm\:ss") + " left";

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
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (Instance == this) Instance = null;
		TaskbarUsageResource.HardwareMonitorBackgroundImageSet -= OnHardwareMonitorBackgroundImageSet;
		PopupWindow.OnCurrentGpuChanged -= OnCurrentGpuChanged;
		DetachCharts();
		StopRefreshTimer();
		GC.SuppressFinalize(this);
	}

	private void DetachCharts()
	{
		DetachChart(CartesianChartMemory);
		DetachChart(CartesianChartVirtualMemory);
		DetachChart(CartesianChartBattery);
		DetachChart(CartesianChartBatteryHealth);
		DetachChart(CartesianChartCpuUsage);
		DetachChart(CartesianChartGpuUsage);
	}

	private static void DetachChart(LiveChartsCore.SkiaSharpView.WinUI.CartesianChart cartesianChart)
	{
		cartesianChart.AutoUpdateEnabled = false;
		cartesianChart.Series = null;
		cartesianChart.XAxes = null;
		cartesianChart.YAxes = null;
		cartesianChart.SyncContext = null;
		cartesianChart.DataContext = null;
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
