using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PinStats.Enums;
using PinStats.Services;
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

	public TaskbarWidgetControl()
	{
		InitializeComponent();
		ApplyConfiguredLayout();

		// Setup the timers to refresh the hardware information
		_refreshTimer = new(RefreshTimerCallback, null, 0, Timeout.Infinite);
		_usageRefreshTimer = new(UsageRefreshTimerCallback, null, 0, Timeout.Infinite);
	}

	private void ApplyConfiguredLayout()
	{
		RootButton.Margin = new Thickness(TaskbarWidgetSettings.RootHorizontalMargin, TaskbarWidgetSettings.RootVerticalMargin, TaskbarWidgetSettings.RootHorizontalMargin, TaskbarWidgetSettings.RootVerticalMargin);
		RootButton.Padding = new Thickness(TaskbarWidgetSettings.RootHorizontalPadding, TaskbarWidgetSettings.RootVerticalPadding, TaskbarWidgetSettings.RootHorizontalPadding, TaskbarWidgetSettings.RootVerticalPadding);
		ItemsPanel.Spacing = TaskbarWidgetSettings.ItemSpacing;

		SetupItemLayout(ItemCpuUsage, TaskbarWidgetItemType.CpuUsage, TaskbarWidgetSettings.PercentItemWidth);
		SetupItemLayout(ItemGpuUsage, TaskbarWidgetItemType.GpuUsage, TaskbarWidgetSettings.PercentItemWidth);
		SetupItemLayout(ItemMemoryUsage, TaskbarWidgetItemType.MemoryUsage, TaskbarWidgetSettings.PercentItemWidth);
		SetupItemLayout(ItemVirtualMemoryUsage, TaskbarWidgetItemType.VirtualMemoryUsage, TaskbarWidgetSettings.PercentItemWidth);
		SetupItemLayout(ItemNetworkSpeed, TaskbarWidgetItemType.NetworkSpeed, TaskbarWidgetSettings.SpeedItemWidth);
		SetupItemLayout(ItemStorageSpeed, TaskbarWidgetItemType.StorageSpeed, TaskbarWidgetSettings.SpeedItemWidth);
		SetupItemLayout(ItemBatteryPercent, TaskbarWidgetItemType.BatteryPercent, TaskbarWidgetSettings.PercentItemWidth);
		SetupItemLayout(ItemBatteryPower, TaskbarWidgetItemType.BatteryPower, TaskbarWidgetSettings.BatteryPowerItemWidth);
	}

	private static void SetupItemLayout(FrameworkElement itemElement, TaskbarWidgetItemType itemType, double itemWidth)
	{
		itemElement.Width = itemWidth;
		itemElement.Visibility = TaskbarWidgetSettings.IsItemEnabled(itemType) ? Visibility.Visible : Visibility.Collapsed;
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

			UpdatePercentUsageItem(ProgressRingCpuUsage, TextBlockCpuUsage, cpuUsage);
			UpdatePercentUsageItem(ProgressRingGpuUsage, TextBlockGpuUsage, gpuUsage);
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

			UpdatePercentUsageItem(ProgressRingMemoryUsage, TextBlockMemoryUsage, memoryUsagePercent);
			UpdatePercentUsageItem(ProgressRingVirtualMemoryUsage, TextBlockVirtualMemoryUsage, virtualMemoryUsagePercent);

			TextBlockNetworkUploadSpeed.Text = networkUploadSpeedText;
			TextBlockNetworkDownloadSpeed.Text = networkDownloadSpeedText;
			TextBlockStorageReadSpeed.Text = storageReadSpeedText;
			TextBlockStorageWriteSpeed.Text = storageWriteSpeedText;

			UpdatePercentUsageItem(ProgressRingBatteryPercent, TextBlockBatteryPercent, batteryPercent);
			TextBlockBatteryPower.Text = batteryChargeRate is null ? _localizationService.GetLocalizedString("Info.NotAvailable") : FormatBatteryChargeRate(batteryChargeRate.Value);
		});
	}

	private static float GetMemoryUsagePercent(bool queryVirtualMemory)
	{
		var totalMemory = HardwareMonitor.GetTotalMemory(queryVirtualMemory);
		if (totalMemory <= 0) return 0;

		var usedMemory = HardwareMonitor.GetUsedMemory(queryVirtualMemory);
		return usedMemory / totalMemory * 100;
	}

	private void UpdatePercentUsageItem(ProgressRing progressRing, TextBlock textBlock, float? usagePercent)
	{
		if (usagePercent is null)
		{
			progressRing.Value = 0;
			textBlock.Text = _localizationService.GetLocalizedString("Info.NotAvailable");
			return;
		}

		progressRing.Value = Math.Clamp(usagePercent.Value, 0, 100);
		textBlock.Text = $"{usagePercent.Value:N0}%";
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
