using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.ViewModels;
using System.Runtime.InteropServices;
using Windows.UI.WindowManagement;
using WinUIEx;

namespace PinStats;

public sealed partial class PopupWindow : IDisposable
{
    // Import the necessary function from user32.dll
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int RefreshTimerIntervalInMilliseconds = 1000;

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public readonly static UsageViewModel GpuUsageViewModel = new();

	public static event EventHandler OnCurrentGpuChanged;

	private Timer _refreshTimer;

	public PopupWindow()
	{
		InitializeComponent();

        // Set window and AppWindow properties
		this.SetIsAlwaysOnTop(true);
		this.SetIsShownInSwitchers(false);

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
    }

    private void Initialize()
	{
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
		var isWindowInFocus = GetForegroundWindow() == this.GetWindowHandle();
        if (!isWindowInFocus) DispatcherQueue.TryEnqueue(Close);

        HardwareMonitor.UpdateCpuHardware();
		HardwareMonitor.UpdateMemoryHardware();
		HardwareMonitor.UpdateNetworkHardware();
		HardwareMonitor.UpdateStorageHardware();
		HardwareMonitor.UpdateCurrentGpuHardware();

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

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm || RuntimeInformation.ProcessArchitecture == Architecture.Arm64) gpuInformationText = "N/A";

        // Define battery information texts
        string batteryInformationText = null;
		string batteryHealthInformationText = null;

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
		else if(_refreshTimer == null)
        {
            // Setup the timer to refresh the hardware information
            _refreshTimer = new(RefreshTimerCallback, null, RefreshTimerIntervalInMilliseconds, Timeout.Infinite); // 1 second (1000 ms)

            // Force the window to be in the foreground
            var hWnd = this.GetWindowHandle();
            var foregroundWindow = GetForegroundWindow();
            var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
			var currentThreadId = GetCurrentThreadId();
			AttachThreadInput(foregroundThreadId, currentThreadId, true);
            SetForegroundWindow(hWnd);

            // Should be called after BringToFront() to prevent the window from being closed when ComboBoxGpuList.SelectedIndex is set.
			// (RefreshHardwareInformation() calls Close() when the window is not in focus)
            Initialize();

            // Refresh the hardware information immediately
            RefreshHardwareInformation();
        }
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
		OnCurrentGpuChanged?.Invoke(this, EventArgs.Empty);
		TextBlockGpuName.Text = HardwareMonitor.GetCurrentGpuName();
		RefreshHardwareInformation();
	}

	private void OnClosed(object sender, WindowEventArgs args) => Dispose();
}
