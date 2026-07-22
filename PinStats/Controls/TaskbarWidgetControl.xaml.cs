using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PinStats.Enums;
using PinStats.Services;
using PinStats.ViewModels;
using PinStats.Views;

namespace PinStats.Controls;

public sealed partial class TaskbarWidgetControl : UserControl, IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 1000;
	private const int UsageRefreshTimerIntervalInMilliseconds = 250;

	private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
	private readonly Timer _refreshTimer;
	private readonly Timer _usageRefreshTimer;
	private bool _disposed;

	// Must be initialized before InitializeComponent() runs since the ItemsRepeater binds to it.
	private List<TaskbarWidgetItemViewModel> WidgetItems { get; } = CreateWidgetItems();

	public TaskbarWidgetControl()
	{
		InitializeComponent();
		ApplyConfiguredLayout();

		// Setup the timers to refresh the hardware information
		_refreshTimer = new(RefreshTimerCallback, null, 0, Timeout.Infinite);
		_usageRefreshTimer = new(UsageRefreshTimerCallback, null, 0, Timeout.Infinite);
	}

	private static List<TaskbarWidgetItemViewModel> CreateWidgetItems() =>
	[.. TaskbarWidgetSettings.GetItemOrder().Where(TaskbarWidgetSettings.IsItemEnabled).Select(itemType => new TaskbarWidgetItemViewModel(itemType))];

	private TaskbarWidgetItemViewModel GetWidgetItem(TaskbarWidgetItemType itemType) => WidgetItems.FirstOrDefault(widgetItem => widgetItem.ItemType == itemType);

	private void ApplyConfiguredLayout()
	{
		RootButton.Margin = new Thickness(TaskbarWidgetSettings.RootHorizontalMargin, TaskbarWidgetSettings.RootVerticalMargin, TaskbarWidgetSettings.RootHorizontalMargin, TaskbarWidgetSettings.RootVerticalMargin);
		RootButton.Padding = new Thickness(TaskbarWidgetSettings.RootHorizontalPadding, TaskbarWidgetSettings.RootVerticalPadding, TaskbarWidgetSettings.RootHorizontalPadding, TaskbarWidgetSettings.RootVerticalPadding);
		ItemsLayout.Spacing = TaskbarWidgetSettings.ItemSpacing;
	}

	private void RefreshTimerCallback(object state)
	{
		try { RefreshWidgetInformation(); }
		catch { } // Ignore. Hardware is unpredictable.
		finally { RestartRefreshTimer(); }
	}

	private void UsageRefreshTimerCallback(object state)
	{
		try { RefreshUsageInformation(); }
		catch { } // Ignore. Hardware is unpredictable.
		finally { RestartUsageRefreshTimer(); }
	}

	private void RestartRefreshTimer()
	{
		if (_disposed) return;

		try { _refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite); }
		catch (ObjectDisposedException) { }
	}

	private void RestartUsageRefreshTimer()
	{
		if (_disposed) return;

		try { _usageRefreshTimer.Change(UsageRefreshTimerIntervalInMilliseconds, Timeout.Infinite); }
		catch (ObjectDisposedException) { }
	}

	private void RefreshUsageInformation()
	{
		if (_disposed) return;

		// If the system is in sleep or hibernate mode, don't update the hardware information.
		if (!HardwareMonitor.ShouldUpdate) return;

		// CPU and GPU usages are updated by their own timers in HardwareMonitor.
		var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();

		DispatcherQueue.TryEnqueue(() =>
		{
			if (_disposed) return;

			UpdatePercentUsageItem(GetWidgetItem(TaskbarWidgetItemType.CpuUsage), cpuUsage);
			UpdatePercentUsageItem(GetWidgetItem(TaskbarWidgetItemType.GpuUsage), gpuUsage);
		});
	}

	private void RefreshWidgetInformation()
	{
		if (_disposed) return;

		// If the system is in sleep or hibernate mode, don't update the hardware information.
		if (!HardwareMonitor.ShouldUpdate) return;

		var updatesMemory = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.MemoryUsage) || TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.VirtualMemoryUsage);
		if (updatesMemory) HardwareMonitor.UpdateMemoryHardware();

		var updatesBattery = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.BatteryPercent) || TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.BatteryPower);
		var hasBattery = updatesBattery && HardwareMonitor.HasBattery();
		if (hasBattery) HardwareMonitor.UpdateBatteryHardware();

		var memoryUsagePercent = GetMemoryUsagePercent(false);
		var virtualMemoryUsagePercent = GetMemoryUsagePercent(true);

		// Network and storage speeds are updated by their own timers in HardwareMonitor.
		var networkUploadSpeedText = FormatSpeed(HardwareMonitor.GetNetworkTotalUploadSpeedInBytes());
		var networkDownloadSpeedText = FormatSpeed(HardwareMonitor.GetNetworkTotalDownloadSpeedInBytes());
		var storageReadSpeedText = FormatSpeed(HardwareMonitor.GetStorageReadRatePerSecondInBytes());
		var storageWriteSpeedText = FormatSpeed(HardwareMonitor.GetStorageWriteRatePerSecondInBytes());

		var batteryPercent = hasBattery ? HardwareMonitor.GetTotalBatteryPercent() : null;
		var batteryChargeRate = hasBattery ? HardwareMonitor.GetTotalBatteryChargeRate() : null;

		DispatcherQueue.TryEnqueue(() =>
		{
			if (_disposed) return;

			UpdatePercentUsageItem(GetWidgetItem(TaskbarWidgetItemType.MemoryUsage), memoryUsagePercent);
			UpdatePercentUsageItem(GetWidgetItem(TaskbarWidgetItemType.VirtualMemoryUsage), virtualMemoryUsagePercent);

			UpdateSpeedUsageItem(GetWidgetItem(TaskbarWidgetItemType.NetworkSpeed), networkUploadSpeedText, networkDownloadSpeedText);
			UpdateSpeedUsageItem(GetWidgetItem(TaskbarWidgetItemType.StorageSpeed), storageReadSpeedText, storageWriteSpeedText);

			UpdatePercentUsageItem(GetWidgetItem(TaskbarWidgetItemType.BatteryPercent), batteryPercent);
			UpdateBatteryPowerItem(batteryChargeRate);
		});
	}

	private static float GetMemoryUsagePercent(bool queryVirtualMemory)
	{
		var totalMemory = HardwareMonitor.GetTotalMemory(queryVirtualMemory);
		if (totalMemory <= 0) return 0;

		var usedMemory = HardwareMonitor.GetUsedMemory(queryVirtualMemory);
		return usedMemory / totalMemory * 100;
	}

	private void UpdatePercentUsageItem(TaskbarWidgetItemViewModel itemViewModel, float? usagePercent)
	{
		if (itemViewModel is null) return;

		if (usagePercent is null)
		{
			itemViewModel.Value = 0;
			itemViewModel.Text = _localizationService.GetLocalizedString("Info.NotAvailable");
			return;
		}

		itemViewModel.Value = Math.Clamp(usagePercent.Value, 0, 100);
		itemViewModel.Text = $"{usagePercent.Value:N0}%";
	}

	private static void UpdateSpeedUsageItem(TaskbarWidgetItemViewModel itemViewModel, string primaryText, string secondaryText)
	{
		if (itemViewModel is null) return;

		itemViewModel.PrimaryText = primaryText;
		itemViewModel.SecondaryText = secondaryText;
	}

	private void UpdateBatteryPowerItem(float? batteryChargeRate)
	{
		var batteryPowerItem = GetWidgetItem(TaskbarWidgetItemType.BatteryPower);
		if (batteryPowerItem is null) return;

		batteryPowerItem.Text = batteryChargeRate is null ? _localizationService.GetLocalizedString("Info.NotAvailable") : FormatBatteryChargeRate(batteryChargeRate.Value);
	}

	private static string FormatBatteryChargeRate(float batteryChargeRate)
	{
		var batteryChargeRatePrefix = batteryChargeRate > 0 ? "+" : string.Empty;
		return batteryChargeRatePrefix + batteryChargeRate.ToString("N1") + " W";
	}

	private static string FormatSpeed(double speedInBytes)
	{
		var kilobytesPerSecond = speedInBytes / 1024;
		if (kilobytesPerSecond < 1024) return $"{kilobytesPerSecond:N0} KB/s";

		var megabytesPerSecond = kilobytesPerSecond / 1024;
		if (megabytesPerSecond < 1024) return $"{megabytesPerSecond:N1} MB/s";

		return $"{megabytesPerSecond / 1024:N1} GB/s";
	}

	private void OnRootButtonClicked(object sender, RoutedEventArgs e) => PopupWindow.ShowNearTaskbar(anchorToCursor: true);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		try { _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); }
		catch (ObjectDisposedException) { }

		try { _usageRefreshTimer.Change(Timeout.Infinite, Timeout.Infinite); }
		catch (ObjectDisposedException) { }

		_refreshTimer.Dispose();
		_usageRefreshTimer.Dispose();
		GC.SuppressFinalize(this);
	}
}
