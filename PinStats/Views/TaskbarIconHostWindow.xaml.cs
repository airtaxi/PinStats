using CommunityToolkit.Mvvm.Messaging;
using Deskband11Lib.Core;
using Deskband11Lib.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Microsoft.Windows.Storage.Pickers;
using PinStats.Enums;
using PinStats.Helpers;
using PinStats.Services;
using PinStats.ViewModels;
using PinStats.ViewModels.Messages;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Image = System.Drawing.Image;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;
using PopupWindow = PinStats.Views.PopupWindow;

namespace PinStats.Views;

public sealed partial class TaskbarIconHostWindow : Window, IRecipient<MonitorListRefreshRequested>, IRecipient<LanguageListRefreshRequested>, IRecipient<TaskbarWidgetMonitorListRefreshRequested>, IRecipient<PopupBackgroundImageSelectionRequested>, IRecipient<HardwareMonitorBackgroundImageSelectionRequested>
{
	private const int UpdateTimerInterval = 250;
	private const int TrayIconImageSize = 64;

	private static readonly PrivateFontCollection PrivateFontCollection = new();
	private static readonly string BinaryDirectory = AppContext.BaseDirectory;
	private static readonly string AssetsDirectory = Path.Combine(BinaryDirectory, "Assets");

	private readonly LocalizationService _localizationService = App.Services.GetRequiredService<LocalizationService>();
	private readonly SystemThemeService _systemThemeService = App.Services.GetRequiredService<SystemThemeService>();
	private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
	private readonly Timer _updateTimer;

	private Image _iconImage;
	private string _iconImagePath;

	public TaskbarIconHostWindowViewModel ViewModel { get; }

	public TaskbarIconHostWindow()
	{
		// Assign the view model before InitializeComponent so that OneTime x:Bind expressions can resolve it.
		ViewModel = new TaskbarIconHostWindowViewModel();

		InitializeComponent(); // This is required to initialize the context menu.

		_ = _dispatcherQueue; // Suppress field-not-used warning; the dispatcher queue is captured for icon rendering callbacks.

		_updateTimer = new(OnUpdateTimerCallback, null, Timeout.Infinite, Timeout.Infinite);

		// Initialize the private font collection used for icon text rendering.
		var fontDirectory = Path.Combine(BinaryDirectory, "Fonts");
		var fontFilePath = Path.Combine(fontDirectory, "Pretendard-ExtraLight.ttf");
		PrivateFontCollection.AddFontFile(fontFilePath);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;

        // Register this window as a messenger recipient so it can refresh dynamic menus and show pickers.
        WeakReferenceMessenger.Default.RegisterAll(this);

		// Initialize the view model (sets observable properties that drive the menu item states).
		ViewModel.Initialize();

		// Refresh dynamic menus for the first time.
		RefreshMonitorListMenuItems();
		RefreshLanguageMenuItems();
		RefreshTaskbarWidgetMonitorMenuItems();

		// Setup the icon image based on the current system theme and keep it in sync with system theme changes.
		UpdateIconImageBySystemTheme();
		_systemThemeService.SystemThemeChanged += OnSystemThemeChanged;

		// Start the update timer to refresh the icon image.
		_updateTimer.Change(UpdateTimerInterval, Timeout.Infinite);
	}

	private void OnClosed(object sender, WindowEventArgs args)
	{
		_updateTimer?.Dispose();
		_systemThemeService.SystemThemeChanged -= OnSystemThemeChanged;
		WeakReferenceMessenger.Default.UnregisterAll(this);
		SystemEvents.DisplaySettingsChanged -= ViewModel.OnDisplaySettingsChanged;
	}

	private void OnUpdateTimerCallback(object state)
	{
		try { _dispatcherQueue.TryEnqueue(Update); }
		finally { _updateTimer.Change(UpdateTimerInterval, Timeout.Infinite); }
	}

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DestroyIcon(IntPtr handle);

	private void OnSystemThemeChanged(object sender, SystemThemeChangedEventArgs args) => _dispatcherQueue.TryEnqueue(UpdateIconImageBySystemTheme);

	private void UpdateIconImageBySystemTheme()
	{
		// The taskbar uses a dark surface when the system is in dark theme, so a white icon is used there and vice versa.
		var useWhiteIcon = !_systemThemeService.IsSystemLightTheme;
		var imageFileName = useWhiteIcon ? "Cpu_White.png" : "Cpu.png";
		_iconImagePath = Path.Combine(AssetsDirectory, imageFileName);
		_iconImage = Image.FromFile(_iconImagePath).GetThumbnailImage(TrayIconImageSize, TrayIconImageSize, null, IntPtr.Zero);
	}

	private void Update()
	{
		// If the system is in sleep or hibernate mode, don't update the icon image.
		if (!HardwareMonitor.ShouldUpdate) return;

		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";

		var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();

		var usage = 0f;
		if (lastUsageTarget == "CPU") usage = cpuUsage;
		else if (lastUsageTarget == "GPU") usage = gpuUsage;
		var usageText = GenerateUsageText(usage);

		// The icon text color follows the system theme so that the usage value stays readable on the taskbar.
		var useWhiteText = !_systemThemeService.IsSystemLightTheme;

		lock (_iconImage)
		{
			var image = _iconImage;
			using var bitmap = new Bitmap(image);
			using var graphics = Graphics.FromImage(bitmap);
			var font = new Font(PrivateFontCollection.Families[0], 30f / App.MainWindowRasterizationScale);
			var stringFormat = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			var rect = new RectangleF(0, 2, image.Width, image.Height);
			graphics.DrawString(usageText, font, useWhiteText ? Brushes.White : Brushes.Black, rect, stringFormat);

			try
			{
				var icon = bitmap.GetHicon();
				try
				{
					TaskbarIconCpuUsage.Icon = System.Drawing.Icon.FromHandle(icon);
					TaskbarIconCpuUsage.ToolTipText = _localizationService.GetFormattedString("Tooltip.UsageFormat", lastUsageTarget, $"{usage:N0}");
				}
				finally { DestroyIcon(icon); } // Destroying the icon handle manually since it's not automatically destroyed.
			}
			catch (ExternalException) { } // Handling rare GDI+ exception.
			catch (InvalidOperationException) { } // Handling rare GDI+ exception.
		}

		UsageHistoryBuffer.AddUsageInformation((int)cpuUsage, (int)gpuUsage);
	}

	private static string GenerateUsageText(float usage)
	{
		usage = Math.Min(usage, 100);

		var usageText = usage.ToString("N0");
		if (double.Parse(usageText) >= 100) usageText = "M"; // Usage can got 100% or more. So, I decided to use "M" instead of "100";
		return usageText;
	}

	private void RefreshMonitorListMenuItems()
	{
		// Clear the existing menu items.
		MenuFlyoutSubItemMonitors.Items.Clear();

		// Add the menu items.
		var monitors = MonitorHelper.GetMonitors();
		var index = 0;
		foreach (var monitor in monitors)
		{
			var resolutionText = $"{monitor.SizeAndPosition.Width}x{monitor.SizeAndPosition.Height}";
			var menuFlyoutItem = new MenuFlyoutItem { Text = _localizationService.GetFormattedString("Menu.MonitorEntry", ++index, monitor.MonitorName, resolutionText) };
			menuFlyoutItem.Click += (_, _) => ViewModel.ShowHardwareMonitorWindowCommand.Execute(monitor);
			MenuFlyoutSubItemMonitors.Items.Add(menuFlyoutItem);
		}
	}

	private void RefreshLanguageMenuItems()
	{
		// Clear the existing menu items.
		MenuFlyoutSubItemLanguage.Items.Clear();

		var currentOverride = Configuration.GetValue<string>("LanguageOverride") ?? string.Empty;

		var systemItem = new ToggleMenuFlyoutItem
		{
			Text = _localizationService.GetLanguageDisplayName(string.Empty),
			IsChecked = string.IsNullOrEmpty(currentOverride)
		};
		systemItem.Click += (_, _) => ViewModel.ApplyLanguageOverrideCommand.Execute(string.Empty);
		MenuFlyoutSubItemLanguage.Items.Add(systemItem);

		foreach (var languageTag in _localizationService.SupportedLanguageTags)
		{
			var languageItem = new ToggleMenuFlyoutItem
			{
				Text = _localizationService.GetLanguageDisplayName(languageTag),
				IsChecked = string.Equals(currentOverride, languageTag, StringComparison.Ordinal)
			};
			languageItem.Click += (_, _) => ViewModel.ApplyLanguageOverrideCommand.Execute(languageTag);
			MenuFlyoutSubItemLanguage.Items.Add(languageItem);
		}
	}

	private void RefreshTaskbarWidgetMonitorMenuItems()
	{
		// TaskbarMonitor is only supported on Windows 11 and later.
		if (!TaskbarHelper.IsWindows11OrGreater()) return;

		MenuFlyoutSubItemTaskbarWidgetMonitor.Items.Clear();

		var availableIdentities = TaskbarMonitor.GetAvailableMonitorIdentities();
		var currentIdentity = TaskbarWidgetSettings.PreferredMonitorIdentity;
		foreach (var identity in availableIdentities)
		{
			var radioItem = new RadioMenuFlyoutItem { Text = GetMonitorIdentityDisplayName(identity), IsChecked = identity == currentIdentity };
			radioItem.Click += (_, _) => ViewModel.SelectTaskbarWidgetMonitorIdentityCommand.Execute(identity);
			MenuFlyoutSubItemTaskbarWidgetMonitor.Items.Add(radioItem);
		}

		// Show the current identity as a disabled item if it is not available anymore. (eg: the monitor has been disconnected)
		if (!availableIdentities.Contains(currentIdentity))
		{
			var radioItem = new RadioMenuFlyoutItem { Text = GetMonitorIdentityDisplayName(currentIdentity), IsChecked = true, IsEnabled = false };
			MenuFlyoutSubItemTaskbarWidgetMonitor.Items.Add(radioItem);
		}
	}

	private string GetMonitorIdentityDisplayName(int identity)
	{
		if (identity <= 0) return _localizationService.GetLocalizedString("Menu.TaskbarWidgetMonitorPrimary");
		return _localizationService.GetFormattedString("Menu.TaskbarWidgetMonitorSecondaryFormat", identity);
	}

	private async Task ShowBackgroundImagePickerAsync(BackgroundImageType backgroundImageType)
	{
		var xamlRoot = TaskbarIconCpuUsage.XamlRoot;
		var fileOpenPicker = new FileOpenPicker(xamlRoot.ContentIslandEnvironment.AppWindowId);
		fileOpenPicker.FileTypeFilter.Add(".png");
		fileOpenPicker.FileTypeFilter.Add(".jpg");
		fileOpenPicker.FileTypeFilter.Add(".jpeg");

		var storageFile = await fileOpenPicker.PickSingleFileAsync();
		if (storageFile is null) return;

		// Configure background image path with prefix string
		var path = storageFile.Path;
		Configuration.SetValue(backgroundImageType.ToString() + "BackgroundImagePath", path);

		ViewModel.RefreshBackgroundImageMenuItemEnabledState();

		if (backgroundImageType == BackgroundImageType.HardwareMonitor) TaskbarIconHostWindowViewModel.RaiseHardwareMonitorBackgroundImageSet();
	}

	public void Receive(MonitorListRefreshRequested message) => _dispatcherQueue.TryEnqueue(RefreshMonitorListMenuItems);

	public void Receive(LanguageListRefreshRequested message) => _dispatcherQueue.TryEnqueue(RefreshLanguageMenuItems);

	public void Receive(TaskbarWidgetMonitorListRefreshRequested message) => _dispatcherQueue.TryEnqueue(RefreshTaskbarWidgetMonitorMenuItems);

	public void Receive(PopupBackgroundImageSelectionRequested message) => _dispatcherQueue.TryEnqueue(async () => await ShowBackgroundImagePickerAsync(BackgroundImageType.Popup));

	public void Receive(HardwareMonitorBackgroundImageSelectionRequested message) => _dispatcherQueue.TryEnqueue(async () => await ShowBackgroundImagePickerAsync(BackgroundImageType.HardwareMonitor));
}