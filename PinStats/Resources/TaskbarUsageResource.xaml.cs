using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using PinStats.Enums;
using PinStats.Helpers;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using WinUIEx;
using Image = System.Drawing.Image;
using Monitor = PinStats.Helpers.MonitorHelper.Monitor;

namespace PinStats.Resources;

public partial class TaskbarUsageResource
{
	private const int UpdateTimerInterval = 250;
	private const int TrayIconImageSize = 64;

	private static readonly PrivateFontCollection PrivateFontCollection = new();
	private static readonly string BinaryDirectory;
	private static readonly string AssetsDirectory;
	private static readonly Timer UpdateTimer;
	private static event EventHandler UpdateTimerElapsed;

	private Image _iconImage;
    private string _iconImagePath;

	// Popup does not need this event since popup and context menu cannot be opened at the same time
	public static event EventHandler HardwareMonitorBackgroundImageSet;
	
    static TaskbarUsageResource()
	{
		BinaryDirectory = AppContext.BaseDirectory;
		AssetsDirectory = Path.Combine(BinaryDirectory, "Assets");

		var fontDirectory = Path.Combine(BinaryDirectory, "Fonts");
		var fontFilePath = Path.Combine(fontDirectory, "Pretendard-ExtraLight.ttf");
		PrivateFontCollection.AddFontFile(fontFilePath);

        // TODO: add a setting to change the interval of the timer.
        UpdateTimer = new(UpdateTimerCallback, null, UpdateTimerInterval, Timeout.Infinite);
    }

    private static void UpdateTimerCallback(object state)
	{
		try { UpdateTimerElapsed?.Invoke(null, EventArgs.Empty); }
		finally { UpdateTimer.Change(UpdateTimerInterval, Timeout.Infinite); }
	}

	public TaskbarUsageResource()
	{
		InitializeComponent(); // This is required to initialize the context menu.

		// Setup MenuFlyoutItem(s)
		RefreshShowHardwareMonitorMenuFlyoutSubItems();
		UpdateSetupStartupProgramMenuFlyoutItemTextProperty();
		UpdateSetupIconColorMenuFlyoutItemTextProperty();
		UpdateVersionNameMenuFlyoutItemTextProperty();
		UpdateBackgroundImageRelatedMenuFlyoutItemsIsEnabledProperty();

		// Update the icon image
		UpdateIconImageByIconColor();

        // Create the taskbar icon
        TaskbarIconCpuUsage.ForceCreate();

		// Setup the timer to update the icon image
		UpdateTimerElapsed += (s, e) => Update();

        // Listen for display settings changes to refresh the hardware monitor menu items
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

	private void UpdateVersionNameMenuFlyoutItemTextProperty()
	{
		var localVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()[..5];
		MenuFlyoutItemVersionName.Text = $"Version {localVersion}";
	}

	private void UpdateBackgroundImageRelatedMenuFlyoutItemsIsEnabledProperty()
	{
		UpdateResetPopupBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
		UpdateResetHardwareMonitorBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
	}

	// Since command doesn't have a tag property, I used a dictionary to map the command to the monitor.
	private Dictionary<XamlUICommand, Monitor> _commandMonitorMap = new();
	private void RefreshShowHardwareMonitorMenuFlyoutSubItems()
	{
		// Clear the existing menu items and the command map.
		_commandMonitorMap.Clear();
		MenuFlyoutSubItemMonitors.Items.Clear();

		// Add the menu items.
		var monitors = MonitorHelper.GetMonitors();
		int index = 0;
		foreach(var monitor in monitors)
		{
			var resolutionText = $"{monitor.SizeAndPosition.Width}x{monitor.SizeAndPosition.Height}";
			var menuFlyoutItem = new MenuFlyoutItem { Text = $"Monitor {++index}: {monitor.MonitorName} ({resolutionText})", Tag = monitor };

			// Setup the command to show the hardware monitor window.
			var command = new XamlUICommand();
			command.ExecuteRequested += OnShowHardwareMonitorMenuFlyoutItemClicked;
			_commandMonitorMap.Add(command, monitor); // Add the monitor to the command since command doesn't have a tag property.
			menuFlyoutItem.Command = command;

			MenuFlyoutSubItemMonitors.Items.Add(menuFlyoutItem);
		}
	}

	private void OnShowHardwareMonitorMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		var monitor = _commandMonitorMap[sender]; // Retrieve the monitor from the command.

		// Close the existing window instance to prevent multiple windows to be opened.
		var existingWindowInstance = MonitorWindow.Instance;
		existingWindowInstance?.Close();

		var monitorWindow = new MonitorWindow(monitor);
		monitorWindow.Activate();
	}

	private void UpdateSetupStartupProgramMenuFlyoutItemTextProperty()
	{
		// If the startup program is set up, change the menu item text to "Remove from Startup".
		if (StartupHelper.IsStartupProgram)
		{
            // If the startup program path is not valid, reinitialize the startup program.
            if (!StartupHelper.IsStartupProgramPathValid)
			{
				// Disable the menu item to prevent the user from clicking the menu item multiple times.
				MenuFlyoutItemSetupStartupProgram.IsEnabled = false;
				MenuFlyoutItemSetupStartupProgram.Text = "Reinitializing Startup Program...";

				// Reinitialize the startup program by calling the SetupStartupProgram method twice.
				StartupHelper.SetupStartupProgram(); // Delete the existing startup program
				StartupHelper.SetupStartupProgram(); // Reinitialize the startup program

				// Reenable the menu item to allow the user to click the menu item again.
				MenuFlyoutItemSetupStartupProgram.IsEnabled = true;
			}
			MenuFlyoutItemSetupStartupProgram.Text = "Remove from Startup";
		}
		// If the startup program is not set up, change the menu item text to "Add to Startup".
		else MenuFlyoutItemSetupStartupProgram.Text = "Add to Startup";
	}

	private void UpdateSetupIconColorMenuFlyoutItemTextProperty()
	{
		var useWhiteIcon = Configuration.GetValue<bool?>("WhiteIcon") ?? false;
		MenuFlyoutItemSetupIconColor.Text = useWhiteIcon ? "Change to Black Icon" : "Change to White Icon";
	}

	private void UpdateIconImageByIconColor()
	{
		var useWhiteIcon = Configuration.GetValue<bool?>("WhiteIcon") ?? false;
		var imageFileName = useWhiteIcon ? "Cpu_white.png" : "Cpu.png";
		_iconImagePath = Path.Combine(AssetsDirectory, imageFileName);
		_iconImage = Image.FromFile(_iconImagePath).GetThumbnailImage(TrayIconImageSize, TrayIconImageSize, null, IntPtr.Zero);
	}

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DestroyIcon(IntPtr handle);

	private void Update()
	{
		// If the system is in sleep or hibernate mode, don't update the icon image.
		if (!HardwareMonitor.ShouldUpdate) return;

		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";
        var useWhiteIcon = Configuration.GetValue<bool?>("WhiteIcon") ?? false;

        var usage = 0f;
		if (lastUsageTarget == "CPU") usage = HardwareMonitor.GetAverageCpuUsage();
		else if (lastUsageTarget == "GPU") usage = HardwareMonitor.GetCurrentGpuUsage();
		string usageText = GenerateUsageText(usage);

		DispatcherQueue.TryEnqueue(() =>
		{
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
				graphics.DrawString(usageText, font, useWhiteIcon ? Brushes.White : Brushes.Black, rect, stringFormat);

				try
				{
					var icon = bitmap.GetHicon();
					try
					{
						TaskbarIconCpuUsage.Icon = System.Drawing.Icon.FromHandle(icon);
						TaskbarIconCpuUsage.ToolTipText = $"{lastUsageTarget} Usage: {usage:N0}%";
					}
					finally { DestroyIcon(icon); } // Destroying the icon handle manually since it's not automatically destroyed.
				}
				catch (ExternalException) { } // Handling rare GDI+ exception.
				catch (InvalidOperationException) { } // Handling rare GDI+ exception.
			}
		});

		var cpuUsage = HardwareMonitor.GetAverageCpuUsage();
		PopupWindow.CpuUsageViewModel.AddUsageInformation((int)cpuUsage);
		MonitorWindow.CpuUsageViewModel.AddUsageInformation((int)cpuUsage);

		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();
		PopupWindow.GpuUsageViewModel.AddUsageInformation((int)gpuUsage);
		MonitorWindow.GpuUsageViewModel.AddUsageInformation((int)gpuUsage);
	}


	private static string GenerateUsageText(float usage)
	{
		usage = Math.Min(usage, 100);

		var usageText = usage.ToString("N0");
		if (double.Parse(usageText) >= 100) usageText = "M"; // Usage can got 100% or more. So, I decided to use "M" instead of "100";
		return usageText;
	}

	private static void ConfigureBackgroundImagePath(BackgroundImageType backgroundImageType)
	{
		// WinUI's FileOpenPicker won't work with elevated application binary for now
		// Use WindowsAPICodePack's CommonOpenFileDialog instead
		var dialog = new CommonOpenFileDialog();
		dialog.Filters.Add(new CommonFileDialogFilter("Image File", "*.png;*.jpg;*.jpeg"));
		if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;

		// Configure background image path with prefix string
		var path = dialog.FileName;
		Configuration.SetValue(backgroundImageType.ToString() + "BackgroundImagePath", path);
	}

	private const int ReportWindowHorizontalOffset = 220;
	private void OnCpuTaskbarIconLeftClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		var reportWindow = new PopupWindow();
		var scale = (double)reportWindow.GetDpiForWindow() / 96; // 96 is the default DPI of Windows.

		var taskbarRect = TaskbarHelper.GetTaskbarRect();
		var taskbarPosition = TaskbarHelper.GetTaskbarPosition();

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
		reportWindow.Activate();
	}

	private void OnCloseProgramMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args) => Environment.Exit(0);

	private void OnSetupStartupProgramMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		StartupHelper.SetupStartupProgram();
		UpdateSetupStartupProgramMenuFlyoutItemTextProperty();
	}

    private void OnSetupIconColorMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
		var wasWhiteIcon = Configuration.GetValue<bool?>("WhiteIcon") ?? false;
		Configuration.SetValue("WhiteIcon", !wasWhiteIcon);
        UpdateSetupIconColorMenuFlyoutItemTextProperty();
		UpdateIconImageByIconColor();
    }

	private void UpdateResetPopupBackgroundImagePathMenuFlyoutItemIsEnabledProperty()
	{
		var backgroundImagePath = Configuration.GetValue<string>("PopupBackgroundImagePath");
		MenuFlyoutItemResetPopupBackgroundImage.IsEnabled = backgroundImagePath != null;
	}

	private void UpdateResetHardwareMonitorBackgroundImagePathMenuFlyoutItemIsEnabledProperty()
	{
		var backgroundImagePath = Configuration.GetValue<string>("HardwareMonitorBackgroundImagePath");
		MenuFlyoutItemResetHardwareMonitorBackgroundImage.IsEnabled = backgroundImagePath != null;
	}

	private void OnResetPopupBackgroundImageMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		Configuration.SetValue("PopupBackgroundImagePath", null);
		UpdateResetPopupBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
	}

	private void OnResetHardwareMonitorBackgroundImageMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		Configuration.SetValue("HardwareMonitorBackgroundImagePath", null);
		UpdateResetHardwareMonitorBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
		HardwareMonitorBackgroundImageSet?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectPopupBackgroundImageMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		ConfigureBackgroundImagePath(BackgroundImageType.Popup);
		UpdateResetPopupBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
	}

	private void OnSelectHardwareMonitorBackgroundImageMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		ConfigureBackgroundImagePath(BackgroundImageType.HardwareMonitor);
		UpdateResetHardwareMonitorBackgroundImagePathMenuFlyoutItemIsEnabledProperty();
		HardwareMonitorBackgroundImageSet?.Invoke(this, EventArgs.Empty);
	}

	private async void OnRefreshHardwareMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		RefreshShowHardwareMonitorMenuFlyoutSubItems();
		await HardwareMonitor.RefreshComputerHardwareAsync();
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs e) => RefreshShowHardwareMonitorMenuFlyoutSubItems();
}