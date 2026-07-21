using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskband11Lib.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.Services;
using PinStats.ViewModels.Messages;
using PinStats.Views;
using System.Reflection;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;

namespace PinStats.ViewModels;

public partial class TaskbarIconHostWindowViewModel : ObservableObject
{
	private static readonly string BinaryDirectory = AppContext.BaseDirectory;
	private static readonly string AssetsDirectory = Path.Combine(BinaryDirectory, "Assets");

	// Popup does not need this event since popup and context menu cannot be opened at the same time
	public static event EventHandler HardwareMonitorBackgroundImageSet;

	public static void RaiseHardwareMonitorBackgroundImageSet() => HardwareMonitorBackgroundImageSet?.Invoke(null, EventArgs.Empty);

	private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
	private readonly ManualSlotPriorityService _manualSlotPriorityService = App.Services.GetRequiredService<ManualSlotPriorityService>();

	static TaskbarIconHostWindowViewModel()
	{
		_ = AssetsDirectory; // Keep static field usage; assets directory is used by the view for icon rendering.
	}

	public void Initialize()
	{
		// Setup menu item states via observable properties (bound through x:Bind).
		RefreshShowHardwareMonitorMenuRequest();
		UpdateSetupStartupProgramMenuItem();
		UpdateVersionNameMenuItem();
		UpdateBackgroundImageRelatedMenuItemsEnabled();
		RefreshLanguageMenuRequest();

		// The taskbar widget menu is only available on Windows 11 and later.
		if (!TaskbarHelper.IsWindows11OrGreater()) IsTaskbarWidgetMenuVisible = Visibility.Collapsed;
		else RefreshTaskbarWidgetMenuStates();

		// Listen for display settings changes to refresh the hardware monitor menu items
		SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
	}

	internal void OnDisplaySettingsChanged(object sender, EventArgs e) => WeakReferenceMessenger.Default.Send(new MonitorListRefreshRequested());

	[ObservableProperty]
	public partial string VersionNameText { get; set; }

	[ObservableProperty]
	public partial string SetupStartupProgramMenuItemText { get; set; }

	[ObservableProperty]
	public partial bool IsSetupStartupProgramMenuItemEnabled { get; set; } = true;

	[ObservableProperty]
	public partial bool IsResetPopupBackgroundImageMenuItemEnabled { get; set; }

	[ObservableProperty]
	public partial bool IsResetHardwareMonitorBackgroundImageMenuItemEnabled { get; set; }

	[ObservableProperty]
	public partial Visibility IsTaskbarWidgetMenuVisible { get; set; } = Visibility.Visible;

	[ObservableProperty]
	public partial bool IsTaskbarWidgetCpuUsageChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetGpuUsageChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetMemoryUsageChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetVirtualMemoryUsageChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetNetworkSpeedChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetStorageSpeedChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetBatteryPercentChecked { get; set; }

	[ObservableProperty]
	public partial bool IsTaskbarWidgetBatteryPowerChecked { get; set; }

	[ObservableProperty]
	public partial Visibility IsBatteryPercentItemVisible { get; set; } = Visibility.Visible;

	[ObservableProperty]
	public partial Visibility IsBatteryPowerItemVisible { get; set; } = Visibility.Visible;

	private void UpdateVersionNameMenuItem()
	{
		var localVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()[..5];
		VersionNameText = _localizationService.GetFormattedString("Menu.Version", localVersion);
	}

	private void UpdateBackgroundImageRelatedMenuItemsEnabled()
	{
		UpdateResetPopupBackgroundImageMenuItemEnabled();
		UpdateResetHardwareMonitorBackgroundImageMenuItemEnabled();
	}

	private void RefreshShowHardwareMonitorMenuRequest() => WeakReferenceMessenger.Default.Send(new MonitorListRefreshRequested());

	private void RefreshLanguageMenuRequest() => WeakReferenceMessenger.Default.Send(new LanguageListRefreshRequested());

	[RelayCommand]
	private static void ShowHardwareMonitorWindow(Monitor monitor)
	{
		// Close the existing window instance to prevent multiple windows to be opened.
		var existingWindowInstance = MonitorWindow.Instance;
		existingWindowInstance?.Close();

		var monitorWindow = new MonitorWindow(monitor);
		monitorWindow.Activate();
	}

	private void UpdateSetupStartupProgramMenuItem()
	{
		// If the startup program is set up, change the menu item text to "Remove from Startup".
		if (StartupHelper.IsStartupProgram)
		{
			// If the startup program path is not valid, reinitialize the startup program.
			if (!StartupHelper.IsStartupProgramPathValid)
			{
				// Disable the menu item to prevent the user from clicking the menu item multiple times.
				IsSetupStartupProgramMenuItemEnabled = false;
				SetupStartupProgramMenuItemText = _localizationService.GetLocalizedString("Menu.ReinitializingStartup");

				// Reinitialize the startup program by calling the SetupStartupProgram method twice.
				StartupHelper.SetupStartupProgram(); // Delete the existing startup program
				StartupHelper.SetupStartupProgram(); // Reinitialize the startup program

				// Reenable the menu item to allow the user to click the menu item again.
				IsSetupStartupProgramMenuItemEnabled = true;
			}
			SetupStartupProgramMenuItemText = _localizationService.GetLocalizedString("Menu.RemoveFromStartup");
		}
		// If the startup program is not set up, change the menu item text to "Add to Startup".
		else SetupStartupProgramMenuItemText = _localizationService.GetLocalizedString("Menu.AddToStartup");
	}

	[RelayCommand]
	private void ApplyLanguageOverride(string languageTag)
	{
		_localizationService.ApplyLanguageTag(languageTag);
		Configuration.SetValue("LanguageOverride", string.IsNullOrEmpty(languageTag) ? null : languageTag);
		RefreshLanguageMenuRequest();
	}

	[RelayCommand]
	private void ShowPopup() => PopupWindow.ShowNearTaskbar();

	[RelayCommand]
	private void RefreshTaskbarWidgetMenuStates()
	{
		// The taskbar widget menu is only available on Windows 11 and later.
		if (!TaskbarHelper.IsWindows11OrGreater()) return;

		IsTaskbarWidgetCpuUsageChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.CpuUsage);
		IsTaskbarWidgetGpuUsageChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.GpuUsage);
		IsTaskbarWidgetMemoryUsageChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.MemoryUsage);
		IsTaskbarWidgetVirtualMemoryUsageChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.VirtualMemoryUsage);
		IsTaskbarWidgetNetworkSpeedChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.NetworkSpeed);
		IsTaskbarWidgetStorageSpeedChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.StorageSpeed);
		IsTaskbarWidgetBatteryPercentChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.BatteryPercent);
		IsTaskbarWidgetBatteryPowerChecked = TaskbarWidgetSettings.IsItemEnabled(TaskbarWidgetItemType.BatteryPower);

		// Battery related items are only visible when the device has a battery.
		var batteryItemsVisibility = HardwareMonitor.HasBattery() ? Visibility.Visible : Visibility.Collapsed;
		IsBatteryPercentItemVisible = batteryItemsVisibility;
		IsBatteryPowerItemVisible = batteryItemsVisibility;

		WeakReferenceMessenger.Default.Send(new TaskbarWidgetMonitorListRefreshRequested());
	}

	[RelayCommand]
	private void SelectTaskbarWidgetMonitorIdentity(int identity)
	{
		if (TaskbarWidgetSettings.PreferredMonitorIdentity == identity) return;

		TaskbarWidgetSettings.PreferredMonitorIdentity = identity;
		App.RelaunchTaskbarWidgetWindow();
	}

	[RelayCommand]
	private async Task ChangeManualSlotPriorityAsync() => await _manualSlotPriorityService.ShowAndApplyAsync();

	[RelayCommand]
	private void ToggleTaskbarWidgetItem(TaskbarWidgetItemType itemType)
	{
		// The menu item has already flipped its checked state when the command is executed, so flip the stored state as well.
		TaskbarWidgetSettings.SetItemEnabled(itemType, !TaskbarWidgetSettings.IsItemEnabled(itemType));
		App.RelaunchTaskbarWidgetWindow();
	}

	[RelayCommand]
	private void CloseProgram() => Environment.Exit(0);

	[RelayCommand]
	private void SetupStartupProgram()
	{
		StartupHelper.SetupStartupProgram();
		UpdateSetupStartupProgramMenuItem();
	}

	private void UpdateResetPopupBackgroundImageMenuItemEnabled()
	{
		var backgroundImagePath = Configuration.GetValue<string>("PopupBackgroundImagePath");
		IsResetPopupBackgroundImageMenuItemEnabled = backgroundImagePath != null;
	}

	private void UpdateResetHardwareMonitorBackgroundImageMenuItemEnabled()
	{
		var backgroundImagePath = Configuration.GetValue<string>("HardwareMonitorBackgroundImagePath");
		IsResetHardwareMonitorBackgroundImageMenuItemEnabled = backgroundImagePath != null;
	}

	public void RefreshBackgroundImageMenuItemEnabledState() => UpdateBackgroundImageRelatedMenuItemsEnabled();

	[RelayCommand]
	private void ResetPopupBackgroundImage()
	{
		Configuration.SetValue("PopupBackgroundImagePath", null);
		UpdateResetPopupBackgroundImageMenuItemEnabled();
	}

	[RelayCommand]
	private void ResetHardwareMonitorBackgroundImage()
	{
		Configuration.SetValue("HardwareMonitorBackgroundImagePath", null);
		UpdateResetHardwareMonitorBackgroundImageMenuItemEnabled();
		HardwareMonitorBackgroundImageSet?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void SelectPopupBackgroundImage() => WeakReferenceMessenger.Default.Send(new PopupBackgroundImageSelectionRequested());

	[RelayCommand]
	private void SelectHardwareMonitorBackgroundImage() => WeakReferenceMessenger.Default.Send(new HardwareMonitorBackgroundImageSelectionRequested());

	[RelayCommand]
	private async Task RefreshHardwaresAsync()
	{
		RefreshShowHardwareMonitorMenuRequest();
		await HardwareMonitor.RefreshComputerHardwareAsync();
	}
}