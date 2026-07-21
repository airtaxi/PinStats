using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.Services;
using PinStats.ViewModels;
using WinUIEx;

namespace PinStats.Views;

public sealed partial class PopupWindow : IDisposable
{
	private const int RefreshTimerIntervalInMilliseconds = 1000;
	private const int ReportWindowHorizontalOffset = 220;

	public static event EventHandler OnCurrentGpuChanged;

	private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
	private readonly UsageViewModel _cpuUsageViewModel;
	private readonly UsageViewModel _gpuUsageViewModel;
	private CancellationTokenSource _refreshCancellationTokenSource;
	private Timer _refreshTimer;
	private int _refreshInProgress;
	private bool _disposed;
	private bool _loadingVisible;
	private string _latestCpuBaseText = string.Empty;
	private string _latestGpuBaseText = string.Empty;
	private Arm64PowerMeterValues _cachedArm64PowerMeterValues;
	private int _powerFetchInProgress;

	// Show the popup window near the taskbar. (eg: above the taskbar when the taskbar is at the bottom)
	// When anchorToCursor is true and the cursor is over the taskbar, the popup is centered on the cursor instead of the tray icon area.
	public static PopupWindow ShowNearTaskbar(bool anchorToCursor = false)
	{
		var reportWindow = new PopupWindow();
		var scale = (double)reportWindow.GetDpiForWindow() / 96; // 96 is the default DPI of Windows.

		var taskbarRect = TaskbarHelper.GetTaskbarRect();
		var taskbarPosition = TaskbarHelper.GetTaskbarPosition();

		if (anchorToCursor && TaskbarHelper.TryGetCursorAnchorPoint(taskbarRect, out var anchorPoint)) MoveNearCursor(reportWindow, scale, taskbarRect, taskbarPosition, anchorPoint);
		else MoveNearTrayIcon(reportWindow, scale, taskbarRect, taskbarPosition);

		reportWindow.Activate();
		return reportWindow;
	}

	// Anchor the popup near the tray icon at the right side of the taskbar.
	private static void MoveNearTrayIcon(PopupWindow reportWindow, double scale, TaskbarHelper.RECT taskbarRect, TaskbarPosition taskbarPosition)
	{
		// Default position is bottom.
		var positionX = taskbarRect.Right - ((reportWindow.Width + ReportWindowHorizontalOffset) * scale);
		var positionY = taskbarRect.Top - (reportWindow.Height * scale);

		switch (taskbarPosition)
		{
			case TaskbarPosition.Top:
				positionX = taskbarRect.Right - (reportWindow.Width + ReportWindowHorizontalOffset) * scale;
				positionY = taskbarRect.Bottom;
				break;
			case TaskbarPosition.Left:
				positionX = taskbarRect.Right;
				positionY = taskbarRect.Bottom - reportWindow.Height * scale;
				break;
			case TaskbarPosition.Right:
				positionX = taskbarRect.Left - reportWindow.Width * scale;
				positionY = taskbarRect.Bottom - reportWindow.Height * scale;
				break;
		}

		reportWindow.Move((int)positionX, (int)positionY);
	}

	// Center the popup on the cursor position and clamp it within the taskbar bounds. (eg: used when the taskbar widget is clicked)
	private static void MoveNearCursor(PopupWindow reportWindow, double scale, TaskbarHelper.RECT taskbarRect, TaskbarPosition taskbarPosition, TaskbarHelper.POINT anchorPoint)
	{
		var windowWidth = reportWindow.Width * scale;
		var windowHeight = reportWindow.Height * scale;

		// Default position is bottom.
		var positionX = anchorPoint.X - (windowWidth / 2);
		var positionY = taskbarRect.Top - windowHeight;

		switch (taskbarPosition)
		{
			case TaskbarPosition.Top:
				positionY = taskbarRect.Bottom;
				break;
			case TaskbarPosition.Left:
				positionX = taskbarRect.Right;
				positionY = anchorPoint.Y - (windowHeight / 2);
				break;
			case TaskbarPosition.Right:
				positionX = taskbarRect.Left - windowWidth;
				positionY = anchorPoint.Y - (windowHeight / 2);
				break;
		}

		// Clamp the popup within the taskbar bounds so it stays on screen.
		if (taskbarPosition is TaskbarPosition.Top or TaskbarPosition.Bottom) positionX = ClampToRange(positionX, taskbarRect.Left, taskbarRect.Right - windowWidth);
		else positionY = ClampToRange(positionY, taskbarRect.Top, taskbarRect.Bottom - windowHeight);

		reportWindow.Move((int)positionX, (int)positionY);
	}

	private static double ClampToRange(double value, double minimum, double maximum)
	{
		if (maximum < minimum) return minimum;
		return Math.Clamp(value, minimum, maximum);
	}

    public PopupWindow()
	{
		_cpuUsageViewModel = new(UsageHistoryMetric.CpuUsage, _localizationService);
		_gpuUsageViewModel = new(UsageHistoryMetric.GpuUsage, _localizationService);
		InitializeComponent();

        // Fetch initial status of loading indicator visibility
        _loadingVisible = GdLoading.Visibility == Visibility.Visible;

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

		LoadUsageHistory();
		UsageHistoryBuffer.UsageInformationAdded += OnUsageInformationAdded;
	}

	private void InitializeInterface()
	{
		// Setup saved usage target
		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";
		var usageViewModel = lastUsageTarget == "GPU" ? _gpuUsageViewModel : _cpuUsageViewModel;
		RadioButtonCpu.IsChecked = usageViewModel == _cpuUsageViewModel;
		RadioButtonGpu.IsChecked = usageViewModel == _gpuUsageViewModel;
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
		CartesianChartUsage.AutoUpdateEnabled = true;
		CartesianChartUsage.Series = usageViewModel.Series;
		CartesianChartUsage.XAxes = usageViewModel.XAxes;
		CartesianChartUsage.YAxes = usageViewModel.YAxes;
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
		var refreshCancellationTokenSource = _refreshCancellationTokenSource;
		if (_disposed || refreshCancellationTokenSource == null) return;
		if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1) return;

		var cancellationToken = refreshCancellationTokenSource.Token;

		try { RefreshHardwareInformation(cancellationToken); }
		catch (OperationCanceledException) { }
		catch { } // Ignore. Hardware is unpredictable.
		finally
		{
			Interlocked.Exchange(ref _refreshInProgress, 0);
			if (_loadingVisible)
			{
				DispatcherQueue.TryEnqueue(() => GdLoading.Visibility = Visibility.Collapsed);
				_loadingVisible = false;
			}
            if (!cancellationToken.IsCancellationRequested) RestartRefreshTimer();
		}
	}

	private void RefreshHardwareInformation(CancellationToken cancellationToken)
	{
		if (_disposed) return;

		cancellationToken.ThrowIfCancellationRequested();
		HardwareMonitor.UpdateCpuHardware();
		cancellationToken.ThrowIfCancellationRequested();
		HardwareMonitor.UpdateMemoryHardware();
		cancellationToken.ThrowIfCancellationRequested();
		HardwareMonitor.UpdateNetworkHardware();
		cancellationToken.ThrowIfCancellationRequested();
		HardwareMonitor.UpdateStorageHardware();
		cancellationToken.ThrowIfCancellationRequested();
		HardwareMonitor.UpdateCurrentGpuHardware();
		cancellationToken.ThrowIfCancellationRequested();

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

		// Define battery information texts
		var batteryInformationText = default(string);
		var batteryHealthInformationText = default(string);

		// If the device has a battery, update the battery information.
		if (HardwareMonitor.HasBattery())
		{
			cancellationToken.ThrowIfCancellationRequested();
			HardwareMonitor.UpdateBatteryHardware();
			cancellationToken.ThrowIfCancellationRequested();

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
			if (batteryEstimatedTime.HasValue) batteryEstimatedTimeText = _localizationService.GetFormattedString("Info.BatteryTimeLeft", batteryEstimatedTime.Value.ToString(@"hh\:mm\:ss"));

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
		var networkInformationText = _localizationService.GetFormattedString("Info.NetworkFormat", $"{networkUploadSpeed:N0}", $"{networkDownloadSpeed:N0}");

		// Storage
		var storageReadRate = HardwareMonitor.GetStorageReadRatePerSecondInBytes() / 1024;
		var storageWriteRate = HardwareMonitor.GetStorageWriteRatePerSecondInBytes() / 1024;
		var storageInformationText = _localizationService.GetFormattedString("Info.StorageFormat", $"{storageReadRate:N0}", $"{storageWriteRate:N0}");

		cancellationToken.ThrowIfCancellationRequested();
		DispatcherQueue.TryEnqueue(() =>
		{
			if (_disposed || cancellationToken.IsCancellationRequested) return;

			TextBlockCpuInformation.Text = cpuInformationText;
			TextBlockGpuInformation.Text = gpuInformationText;
			TextBlockMemoryInformation.Text = memoryInformationText;
			TextBlockVirtualMemoryInformation.Text = virtualMemoryInformationText;
			TextBlockNetworkInformation.Text = networkInformationText;
			TextBlockStorageInformation.Text = storageInformationText;
			if (batteryInformationText != null) TextBlockBatteryInformation.Text = batteryInformationText;
			if (batteryHealthInformationText != null) TextBlockBatteryHealthInformation.Text = batteryHealthInformationText;
		});

		if (isArm64 && Interlocked.Exchange(ref _powerFetchInProgress, 1) == 0)
		{
			_ = Task.Run(() =>
			{
				try
				{
					if (_disposed || cancellationToken.IsCancellationRequested) return;
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

		Activated -= OnActivated;
		Closed -= OnClosed;
		UsageHistoryBuffer.UsageInformationAdded -= OnUsageInformationAdded;

		StopRefreshTimer();
		GC.SuppressFinalize(this);
	}

	private void RestartRefreshTimer()
	{
		if (_disposed || _refreshTimer == null) return;

		try { _refreshTimer.Change(RefreshTimerIntervalInMilliseconds, Timeout.Infinite); }
		catch (ObjectDisposedException) { }
	}

	private void StopRefreshTimer()
	{
		_refreshCancellationTokenSource?.Cancel();

		if (_refreshTimer != null)
		{
			try { _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite); }
			catch (ObjectDisposedException) { }

			_refreshTimer.Dispose();
			_refreshTimer = null;
		}
		_refreshCancellationTokenSource?.Dispose();
		_refreshCancellationTokenSource = null;
	}

    private void OnActivated(object sender, WindowActivatedEventArgs args)
	{
		// Close the window when the window lost its focus.
		if (args.WindowActivationState == WindowActivationState.Deactivated)
		{
		    Close();
		}
	}

	private void OnRadioButtonClicked(object sender, RoutedEventArgs e)
	{
		if (_disposed || sender is not RadioButton radioButton) return;

		RadioButtonCpu.IsChecked = false;
		RadioButtonGpu.IsChecked = false;
		radioButton.IsChecked = true;

		var usageViewModel = radioButton == RadioButtonGpu ? _gpuUsageViewModel : _cpuUsageViewModel;
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
		InitializeInterface();

		// Setup the timer to refresh the hardware information.
		_refreshCancellationTokenSource = new();
		_refreshTimer = new(RefreshTimerCallback, null, 0, Timeout.Infinite);
	}

	private void OnClosed(object sender, WindowEventArgs args) => Dispose();

	private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();
}
