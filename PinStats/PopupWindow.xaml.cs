using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.ViewModels;
using WinUIEx;

namespace PinStats;

public sealed partial class PopupWindow : IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 1000;

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public readonly static UsageViewModel GpuUsageViewModel = new();

	public static event EventHandler OnCurrentGpuChanged;

	private Timer _refreshTimer;
	private bool _disposed;

	public PopupWindow()
	{
		InitializeComponent();

		// Disable window animations and set dark mode if
		WindowHelper.DisableWindowAnimations(this);

		// Fix white flickering issue when the window is first shown
		if ((Content as FrameworkElement).RequestedTheme == ElementTheme.Dark) WindowHelper.SetDarkModeWindow(this);

		// Set window and AppWindow properties
		this.SetIsAlwaysOnTop(true);
		AppWindow.IsShownInSwitchers = false;

		// Hide the title bar
		ExtendsContentIntoTitleBar = true; // Should be set after WinUI 1.6
		(AppWindow.Presenter as OverlappedPresenter).SetBorderAndTitleBar(true, false);

		// Apply background image if available or use Mica backdrop
		if (!BackgroundImageHelper.TrySetupBackgroundImage(BackgroundImageType.Popup, ImageBackground))
		{
			GridBackground.Visibility = Visibility.Collapsed;
			SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
		}
		// No need to set the backdrop to null since it is already null by default

		// Set the process priority to high to improve performance.
		Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
	}

	private void InitializeInterface()
	{
		// Setup saved usage target
		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";
		var usageViewModel = lastUsageTarget == "GPU" ? GpuUsageViewModel : CpuUsageViewModel;
		RadioButtonCpu.IsChecked = usageViewModel == CpuUsageViewModel;
		RadioButtonGpu.IsChecked = usageViewModel == GpuUsageViewModel;
		ApplyUsageViewModel(usageViewModel);

		// If the device has more than one GPU, show the GPU selection UI.
		var gpuNames = HardwareMonitor.GetGpuHardwareNames();
		if (gpuNames.Count > 1)
		{
			ComboBoxGpuList.ItemsSource = gpuNames;
			ComboBoxGpuList.SelectedIndex = Configuration.GetValue<int?>("GpuIndex") ?? 0;
		}
		else ButtonSelectGpu.Visibility = Visibility.Collapsed;

		// Setup battery related UI elements
		var hasBattery = HardwareMonitor.HasBattery();
		if (!hasBattery)
		{
			GridBattery.Visibility = Visibility.Collapsed;
			GridBatteryHealth.Visibility = Visibility.Collapsed;
		}
		else
		{
			var batteryHealthPercent = HardwareMonitor.GetAverageBatteryHealthPercent();
			var hasBatteryHealth = batteryHealthPercent != null;
			if (!hasBatteryHealth) GridBatteryHealth.Visibility = Visibility.Collapsed;
		}

		// Set the text of the CPU name and GPU name.
		TextBlockCpuName.Text = HardwareMonitor.GetCpuName();
		TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();
	}

	private void ApplyUsageViewModel(UsageViewModel usageViewModel)
	{
		usageViewModel.RefreshSync(); // Renew the "sync" of the UsageViewModel to prevent the chart from not being properly displayed.
		CartesianChartUsage.AutoUpdateEnabled = true;
		CartesianChartUsage.Series = usageViewModel.Series;
		CartesianChartUsage.XAxes = usageViewModel.XAxes;
		CartesianChartUsage.YAxes = usageViewModel.YAxes;
		CartesianChartUsage.SyncContext = usageViewModel.Sync;
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

		// CPU
		var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
		var arm64PowerMeterValues = HardwareMonitor.IsArm64Architecture ? HardwareMonitor.GetArm64PowerMeterValues() : default;

		var cpuInformationText = $"{cpuUsage:N0}%";
		if (HardwareMonitor.IsArm64Architecture)
		{
			cpuInformationText += $" / CPU {arm64PowerMeterValues.TotalCpuPackagePower:N0} W / SoC {arm64PowerMeterValues.SystemOnChipPower:N0} W / Sys {arm64PowerMeterValues.SystemPower:N0} W";
		}
		else
		{
			var cpuTemperature = HardwareMonitor.GetAverageCpuTemperature();
			var cpuPower = HardwareMonitor.GetTotalCpuPackagePower();
			var cpuTemperatureText = cpuTemperature != null ? (" / " + cpuTemperature.Value.ToString("N0") + "°C") : "";
			var cpuPowerText = cpuPower != 0 ? (" / " + cpuPower.ToString("N0") + " W") : "";
			cpuInformationText += cpuTemperatureText + cpuPowerText;
		}

		// GPU
		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();

		var gpuInformationText = $"{gpuUsage:N0}%";
		if (HardwareMonitor.IsArm64Architecture)
		{
			gpuInformationText += $" / GPU {arm64PowerMeterValues.CurrentGpuPower:N0} W / MM {arm64PowerMeterValues.MultimediaPower:N0} W";
		}
		else
		{
			var gpuTemperature = HardwareMonitor.GetCurrentGpuTemperature();
			var gpuPower = HardwareMonitor.GetCurrentGpuPower();
			var gpuTemperatureText = gpuTemperature != null ? (" / " + gpuTemperature.Value.ToString("N0") + "°C") : "";
			var gpuPowerText = gpuPower != 0 ? (" / " + gpuPower.ToString("N0") + " W") : "";
			gpuInformationText += gpuTemperatureText + gpuPowerText;
		}

		// Define battery information texts
		var batteryInformationText = default(string);
		var batteryHealthInformationText = default(string);

		// If the device has a battery, update the battery information.
		if (HardwareMonitor.HasBattery())
		{
			HardwareMonitor.UpdateBatteryHardware();

			var batteryPercentage = HardwareMonitor.GetTotalBatteryPercent().Value; // Assume that the device has a battery due to the if statement above.
			var batteryChargeRate = HardwareMonitor.GetTotalBatteryChargeRate();
			var batteryEstimatedTime = HardwareMonitor.GetTotalBatteryEstimatedTime();

			// Battery charge rate and estimated time text
			var batteryChargeRateText = string.Empty;
			if (batteryChargeRate.HasValue)
			{
				var batteryChargeRatePrefix = string.Empty;
				if (batteryChargeRate.Value > 0) batteryChargeRatePrefix = "+";
				batteryChargeRateText = " / " + batteryChargeRatePrefix + batteryChargeRate.Value.ToString("N1") + " W";
			}
			var batteryEstimatedTimeText = string.Empty;
			if (batteryEstimatedTime.HasValue) batteryEstimatedTimeText = " / " + batteryEstimatedTime.Value.ToString(@"hh\:mm\:ss") + " left";

			// Battery information text
			batteryInformationText = $"{batteryPercentage:N0}%" + batteryChargeRateText + batteryEstimatedTimeText;

			// Battery health information text
			var batteryHealthPercent = HardwareMonitor.GetAverageBatteryHealthPercent();
			if (batteryHealthPercent.HasValue) batteryHealthInformationText = $"{batteryHealthPercent.Value:N0}%";
		}

		// Memory
		var memoryInformationText = HardwareMonitor.GetMemoryInformationText();
		var virtualMemoryInformationText = HardwareMonitor.GetMemoryInformationText(true);

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
			TextBlockMemoryInformation.Text = memoryInformationText;
			TextBlockVirtualMemoryInformation.Text = virtualMemoryInformationText;
			TextBlockNetworkInformation.Text = networkInformationText;
			TextBlockStorageInformation.Text = storageInformationText;
			if (batteryInformationText != null) TextBlockBatteryInformation.Text = batteryInformationText;
			if (batteryHealthInformationText != null) TextBlockBatteryHealthInformation.Text = batteryHealthInformationText;
		});
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Activated -= OnActivated;
		Closed -= OnClosed;

		DetachCharts();
		StopRefreshTimer();
		GC.SuppressFinalize(this);
	}

	private void DetachCharts() => DetachChart(CartesianChartUsage);

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
		if (_disposed || _refreshTimer == null) return;

		try { _refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite); }
		catch (ObjectDisposedException) { }
	}

	private void StopRefreshTimer()
	{
		if (_refreshTimer == null) return;

		try { _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); }
		catch (ObjectDisposedException) { }

		_refreshTimer.Dispose();
		_refreshTimer = null;
	}

	private void OnActivated(object sender, WindowActivatedEventArgs args)
	{
		// Close the window when the window lost its focus.
		if (args.WindowActivationState == WindowActivationState.Deactivated) Close();
	}

	private void OnRadioButtonClicked(object sender, RoutedEventArgs e)
	{
		if (_disposed || sender is not RadioButton radioButton) return;

		RadioButtonCpu.IsChecked = false;
		RadioButtonGpu.IsChecked = false;
		radioButton.IsChecked = true;

		var usageViewModel = radioButton == RadioButtonGpu ? GpuUsageViewModel : CpuUsageViewModel;
		Configuration.SetValue("LastUsageTarget", radioButton == RadioButtonGpu ? "GPU" : "CPU");
		ApplyUsageViewModel(usageViewModel);
	}

	private void OnGpuListComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_disposed || sender is not ComboBox comboBox) return;

		var index = comboBox.SelectedIndex;
		Configuration.SetValue("GpuIndex", index);
		OnCurrentGpuChanged?.Invoke(this, EventArgs.Empty);
		TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (_disposed || _refreshTimer != null) return;

		// Setup the timer to refresh the hardware information
		_refreshTimer = new(RefreshTimerCallback, null, RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)

		// Force the window to be in the foreground
		//var windowHandle = this.GetWindowHandle();
		//var foregroundWindow = WindowHelper.GetForegroundWindow();
		//var foregroundThreadIdentifier = WindowHelper.GetWindowThreadProcessId(foregroundWindow, out _);
		//var currentThreadIdentifier = WindowHelper.GetCurrentThreadId();
		//WindowHelper.AttachThreadInput(foregroundThreadIdentifier, currentThreadIdentifier, true);
		//try { WindowHelper.SetForegroundWindow(windowHandle); }
		//finally { WindowHelper.AttachThreadInput(foregroundThreadIdentifier, currentThreadIdentifier, false); }

		BringToFront();

		// Should be called after BringToFront() to prevent the window from being closed when ComboBoxGpuList.SelectedIndex is set.
		// (RefreshHardwareInformation() calls Close() when the window is not in focus)
		InitializeInterface();

		// Refresh the hardware information immediately
		RefreshHardwareInformation();
	}

	private void OnClosed(object sender, WindowEventArgs args) => Dispose();

	private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();
}
