using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PinStats.Helpers;
using PinStats.ViewModels;

namespace PinStats;

public sealed partial class ReportWindow : IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 1000;

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public readonly static UsageViewModel GpuUsageViewModel = new();

	private readonly Timer _refreshTimer;

	public ReportWindow()
	{
		InitializeComponent();

		// Set window and appwindow properties
		SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
		AppWindow.IsShownInSwitchers = false;
		(AppWindow.Presenter as OverlappedPresenter).SetBorderAndTitleBar(true, false);

		// If OverlappedPresenter's border and titlebar is manually set, the window will not be rounded.
		// So we need to set the window corner to rounded corner manually.
		WindowHelper.SetWindowCornerToRoundedCorner(this);

		// Setup saved usage target
		UsageViewModel usageViewModel = null;
		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";
		if (lastUsageTarget == "CPU")
		{
			usageViewModel = CpuUsageViewModel;
			RadioButtonCpu.IsChecked = true;
		}
		else if (lastUsageTarget == "GPU")
		{
			usageViewModel = GpuUsageViewModel;
			RadioButtonGpu.IsChecked = true;
		}
		usageViewModel?.RefreshSync(); // Renew the "sync" of the UsageViewModel to prevent the chart from not being properly displayed.
		CartesianChartUsage.DataContext = usageViewModel;

		// If the device has more than one GPU, show the GPU selection UI.
		var gpuNames = HardwareMonitor.GetGpuHardwareNames();
		if (gpuNames.Count > 1)
		{
			foreach (var name in gpuNames)
				ComboBoxGpuList.Items.Add(name);
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

		// Refresh the hardware information manually since timer callback is not yet triggered.
		RefreshHardwareInformation();

		// Setup the timer to refresh the hardware information.
		_refreshTimer = new(RefreshTimerCallback, null, RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)
	}

	private void RefreshTimerCallback(object state)
	{
		RefreshHardwareInformation();
		
		// If the window is closed, stop the timer.
		if (_disposed) return;
		_refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)
	}

	private void RefreshHardwareInformation()
	{
		HardwareMonitor.UpdateCpuHardwares();
		HardwareMonitor.UpdateMemoryHardwares();
		HardwareMonitor.UpdateNetworkHardwares();
		HardwareMonitor.UpdateStorageHardwares();
		HardwareMonitor.UpdateCurrentGpuHardware();

		// CPU
		var cpuUage = HardwareMonitor.GetAverageCpuUsage();
		var cpuTemperature = HardwareMonitor.GetAverageCpuTemperature();

		var cpuInformationText = $"{cpuUage:N0}%";
		var cpuTempertureText = cpuTemperature != null ? (" / " + cpuTemperature.Value.ToString("N0") + "�C") : "";
		var cpuPowerText = HardwareMonitor.GetTotalCpuPackagePower() != 0 ? (" / " + HardwareMonitor.GetTotalCpuPackagePower().ToString("N0") + " W") : "";
		cpuInformationText += cpuTempertureText + cpuPowerText;

		// GPU
		var gpuUage = HardwareMonitor.GetCurrentGpuUsage();
		var gpuTemperature = HardwareMonitor.GetCurrentGpuTemperature();
		var gpuPower = HardwareMonitor.GetCurrentGpuPower();

		var gpuInformationText = $"{gpuUage:N0}%";
		var gpuTempertureText = gpuTemperature != null ? (" / " + gpuTemperature.Value.ToString("N0") + "�C") : "";
		var gpuPowerText = gpuPower != 0 ? (" / " + gpuPower.ToString("N0") + " W") : "";
		gpuInformationText += gpuTempertureText + gpuPowerText;

		// Battery
		string batteryInformationText = null;
		string batteryHealthInformationText = null;
		if (HardwareMonitor.HasBattery()) // If the device has a battery, update the battery information.
		{
			HardwareMonitor.UpdateBatteryHardwares();

			var batteryPercentage = HardwareMonitor.GetTotalBatteryPercent();
			var batteryChargeRate = HardwareMonitor.GetTotalBatteryChargeRate();
			var batteryHealthPercent = HardwareMonitor.GetAverageBatteryHealthPercent();

			string batteryChargeRatePrefix = string.Empty;
			if (batteryChargeRate.Value > 0) batteryChargeRatePrefix = "+";
			var batteryChargeRateText = batteryChargeRate != null ? (" / " + batteryChargeRatePrefix + batteryChargeRate.Value.ToString("N1") + " W") : "";
			batteryInformationText = $"{batteryPercentage:N0}%" + batteryChargeRateText;

			var hasBatteryHealth = batteryHealthPercent != null;
			if (hasBatteryHealth) batteryHealthInformationText = $"{batteryHealthPercent:N0}%";
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

	private bool _disposed;
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Activated -= OnActivated;
		Closed -= OnClosed;
		_refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer.
		_refreshTimer.Dispose();
		GC.SuppressFinalize(this);
	}

	private void OnActivated(object sender, WindowActivatedEventArgs args)
	{
		// Close the window when the window lost its focus.
		if (args.WindowActivationState == WindowActivationState.Deactivated) Close();
	}

	private void OnRadioButtonClicked(object sender, RoutedEventArgs e)
	{
		var radioButton = sender as RadioButton;
		RadioButtonCpu.IsChecked = false;
		RadioButtonGpu.IsChecked = false;
		radioButton.IsChecked = true;
		UsageViewModel usageViewModel = null;
		if (radioButton == RadioButtonCpu)
		{
			usageViewModel = CpuUsageViewModel;
			Configuration.SetValue("LastUsageTarget", "CPU");
		}
		else if (radioButton == RadioButtonGpu)
		{
			usageViewModel = GpuUsageViewModel;
			Configuration.SetValue("LastUsageTarget", "GPU");
		}
		usageViewModel?.RefreshSync(); // Renew the "sync" of the UsageViewModel to prevent the chart from not being properly displayed.
		CartesianChartUsage.DataContext = usageViewModel;
	}

	private void OnGpuListComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		var comboBox = sender as ComboBox;
		var index = comboBox.SelectedIndex;
		Configuration.SetValue("GpuIndex", index);
		TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();
		RefreshHardwareInformation();
	}

	private void OnClosed(object sender, WindowEventArgs args) => Dispose();
}
