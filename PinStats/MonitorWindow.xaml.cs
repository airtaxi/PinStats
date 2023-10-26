using LibreHardwareMonitor.Hardware.Cpu;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.Helpers;
using PinStats.ViewModels;
using WinUIEx;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;

namespace PinStats;

public sealed partial class MonitorWindow : IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 500;

	public static MonitorWindow Instance { get; set; }

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public readonly static UsageViewModel GpuUsageViewModel = new();

	public readonly TotalUsageViewModel _memoryUsageViewModel = new();
	public readonly TotalUsageViewModel _virtualMemoryUsageViewModel = new();

	public readonly TotalUsageViewModel _batteryViewModel = new();
	public readonly TotalUsageViewModel _batteryHealthViewModel = new();

	private readonly Timer _refreshTimer;

	public MonitorWindow(Monitor monitor)
	{
		Instance = this;
		InitializeComponent();

		ExtendsContentIntoTitleBar = true;
		SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
		AppWindow.IsShownInSwitchers = false;
		MonitorHelper.PositionWindowToMonitor(this.GetWindowHandle(), monitor);
		AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

		InitializeControls();
		RefreshHardwareInformation();

		_refreshTimer = new(RefreshTimerCallback, null, RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)
	}

	private void InitializeControls()
	{
		TextBlockMotherboardName.Text = HardwareMonitor.GetMotherboardName();
		TextBlockCpuName.Text = HardwareMonitor.GetCpuName();
		TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();

		CartesianChartMemory.DataContext = _memoryUsageViewModel;
		CartesianChartVirtualMemory.DataContext = _virtualMemoryUsageViewModel;

		// Setup CPU usage chart
		CpuUsageViewModel.RefreshSync();
		CartesianChartCpuUsage.DataContext = CpuUsageViewModel;

		// Setup GPU usage chart
		GpuUsageViewModel.RefreshSync();
		CartesianChartGpuUsage.DataContext = GpuUsageViewModel;
		ReportWindow.OnCurrentGpuChanged += OnCurrentGpuChanged;


		CartesianChartBattery.DataContext = _batteryViewModel;
		CartesianChartBatteryHealth.DataContext = _batteryHealthViewModel;

		_batteryViewModel.SetValue(100, 0, "N/A");
		_batteryHealthViewModel.SetValue(100, 0, "N/A");
		//var hasBattery = HardwareMonitor.HasBattery();
		//if (!hasBattery)
		//{
		//}
	}

	private void RefreshTimerCallback(object state)
	{
		try { RefreshHardwareInformation(); }
		finally
		{
			if (!_disposed)
				_refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite);
		}
	}

	private void RefreshHardwareInformation()
	{
		HardwareMonitor.UpdateCpuHardwares();
		HardwareMonitor.UpdateMemoryHardwares();
		HardwareMonitor.UpdateNetworkHardwares();
		HardwareMonitor.UpdateStorageHardwares();
		HardwareMonitor.UpdateCurrentGpuHardware();
		if (HardwareMonitor.HasBattery()) HardwareMonitor.UpdateBatteryHardwares();

		// CPU
		var cpuUage = HardwareMonitor.GetAverageCpuUsage();
		var cpuTemperature = HardwareMonitor.GetAverageCpuTemperature();

		var cpuInformationText = $"{cpuUage:N0}%";
		var cpuTempertureText = cpuTemperature != null ? (" / " + cpuTemperature.Value.ToString("N0") + "°C") : "";
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
			TextBlockCpuInformation.Text = cpuInformationText;
			TextBlockGpuInformation.Text = gpuInformationText;
			_memoryUsageViewModel.SetValue(totalMemory, usedMemory, memoryInformationText);
			_virtualMemoryUsageViewModel.SetValue(totalVirtualMemory, usedVirtualMemory, virtualMemoryInformationText);

			if (HardwareMonitor.HasBattery()) // If the device has a battery, update the battery information.
			{
				var batteryPercentage = HardwareMonitor.GetTotalBatteryPercent().Value; // Assume that the device has a battery due to the if statement above.
				var batteryChargeRate = HardwareMonitor.GetTotalBatteryChargeRate();

				string batteryChargeRatePrefix = string.Empty;
				if (batteryChargeRate.Value > 0) batteryChargeRatePrefix = "+";
				var batteryChargeRateText = batteryChargeRate != null ? (" / " + batteryChargeRatePrefix + batteryChargeRate.Value.ToString("N1") + " W") : "";
				var batteryViewModelDataLabelText = $"{batteryPercentage:N0}%" + batteryChargeRateText;
				_batteryViewModel.SetValue(100, batteryPercentage, batteryViewModelDataLabelText);

				var batteryHealthPercent = HardwareMonitor.GetAverageBatteryHealthPercent();
				var hasBatteryHealth = batteryHealthPercent != null;
				if (hasBatteryHealth)
				{
					var batteryHealthViewModelDataLabelText = $"{batteryHealthPercent:N0}%";
					_batteryHealthViewModel.SetValue(100, batteryHealthPercent.Value, batteryHealthViewModelDataLabelText);
				}
			}

			TextBlockNetworkInformation.Text = networkInformationText;
			TextBlockStorageInformation.Text = storageInformationText;
		});
	}

	private bool _disposed;
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Instance = null;
		ReportWindow.OnCurrentGpuChanged -= OnCurrentGpuChanged;
		_refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer.
		_refreshTimer.Dispose();
		GC.SuppressFinalize(this);
	}

	private void OnCurrentGpuChanged(object sender, EventArgs e) => TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();

	private void OnExitButtonClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Close();

	private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args) => Dispose();
}
