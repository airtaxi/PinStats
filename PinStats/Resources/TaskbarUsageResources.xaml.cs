using HidSharp.Reports;
using Microsoft.UI.Xaml.Input;
using PinStats.Enums;
using PinStats.Helpers;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinUIEx;

namespace PinStats.Resources;

public partial class TaskbarUsageResources
{
	private const int UpdateTimerInterval = 250;
	private const int TrayIconSize = 64;
	private static string BinaryDirectory;

	private readonly static PrivateFontCollection PrivateFontCollection = new();

	private static Timer UpdateTimer;
	private Image _iconImage;

	private string assetsPath;
	private string iconImagePath;


	static TaskbarUsageResources()
	{
		BinaryDirectory = AppContext.BaseDirectory;
		var fontDirectory = Path.Combine(BinaryDirectory, "Fonts");
		var fontFilePath = Path.Combine(fontDirectory, "Pretendard-ExtraLight.ttf");
		PrivateFontCollection.AddFontFile(fontFilePath);
	}

	public TaskbarUsageResources()
	{
		InitializeComponent();
		UpdateSetupStartupProgramMenuFLyoutItemTextProperty();
		UpdateSetupColorMenuFlyoutItemTextProperty();

        // TODO: add a setting to change the interval of the timer.
        UpdateTimer = new(UpdateTimerCallback, null, UpdateTimerInterval, Timeout.Infinite);

		assetsPath = Path.Combine(BinaryDirectory, "Assets");
		var isWhiteIcon = Configuration.GetValue<bool>("WhiteIcon");
		var _imageFileName = (isWhiteIcon ? "Cpu_white.png" : "Cpu.png");
		iconImagePath = Path.Combine(assetsPath, _imageFileName);

		_iconImage = Image.FromFile(iconImagePath).GetThumbnailImage(TrayIconSize, TrayIconSize, null, IntPtr.Zero);
		Update();
		TaskbarIconCpuUsage.ForceCreate();

		var localVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()[..5];
		MenuFlyoutItemVersionName.Text = $"Version {localVersion}";
	}

	private void UpdateTimerCallback(object state)
	{
		try { Update(); }
		finally { UpdateTimer.Change(UpdateTimerInterval, Timeout.Infinite); }
	}

	private void UpdateSetupStartupProgramMenuFLyoutItemTextProperty()
	{
		var isStartupProgram = StartupHelper.IsStartupProgram;
		if (isStartupProgram) MenuFlyoutItemSetupStartupProgram.Text = "Remove from Startup";
		else MenuFlyoutItemSetupStartupProgram.Text = "Add to Startup";
	}

	private void UpdateSetupColorMenuFlyoutItemTextProperty()
	{
		var isWhiteIcon = Configuration.GetValue<bool>("WhiteIcon");
        MenuFlyoutItemSetupColor.Text = (isWhiteIcon) ? "Change to Black Icon" : "Change to White Icon";

	}

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DestroyIcon(IntPtr handle);

	private void Update()
	{
		var lastUsageTarget = Configuration.GetValue<string>("LastUsageTarget") ?? "CPU";
        var isWhiteIcon = Configuration.GetValue<bool>("WhiteIcon");

        float usage = 0f;
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

				var font = new Font(PrivateFontCollection.Families[0], 12);
				var stringFormat = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center
				};
				var rect = new RectangleF(0, 2, image.Width, image.Height);
				graphics.DrawString(usageText, font, (isWhiteIcon)? Brushes.White : Brushes.Black, rect, stringFormat);

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
		ReportWindow.CpuUsageViewModel.AddUsageInformation((int)cpuUsage);

		var gpuUsage = HardwareMonitor.GetCurrentGpuUsage();
		ReportWindow.GpuUsageViewModel.AddUsageInformation((int)gpuUsage);
	}


	private static string GenerateUsageText(float usage)
	{
		usage = Math.Min(usage, 100);

		var usageText = usage.ToString("N0");
		if (usage >= 100) usageText = "M"; // Usage can got 100% or more. So, I decided to use "M" instead of "100";
		return usageText;
	}

	private const int ReportWindowHorizontalOffset = 220;
	private void OnCpuTaskbarIconLeftClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		var reportWindow = new ReportWindow();
		var scale = (double)reportWindow.GetDpiForWindow() / 96; // 96 is the default DPI of Windows.

		var TaskbarRect = TaskbarHelper.GetTaskbarRect();
		TaskbarPosition TaskbarPosition = TaskbarHelper.GetTaskbarPosition();

		// Default position is bottom.
		double positionX = TaskbarRect.Right - ((reportWindow.Width + ReportWindowHorizontalOffset) * scale);
		double positionY = TaskbarRect.Top - (reportWindow.Height * scale);

		if(TaskbarPosition == TaskbarPosition.Top)
		{
			positionX = TaskbarRect.Right - ((reportWindow.Width + ReportWindowHorizontalOffset) * scale);
			positionY = TaskbarRect.Bottom;
		}
		else if(TaskbarPosition == TaskbarPosition.Left)
		{
			positionX = TaskbarRect.Left;
			positionY = TaskbarRect.Bottom - (reportWindow.Height * scale);
		}
		else if(TaskbarPosition == TaskbarPosition.Right)
		{
			positionX = TaskbarRect.Right - (reportWindow.Width * scale);
			positionY = TaskbarRect.Bottom - (reportWindow.Height * scale);
		}

		reportWindow.Move((int)positionX, (int)positionY);
		reportWindow.Activate();
		reportWindow.BringToFront();
	}

	private void OnCloseProgramMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args) => Environment.Exit(0);

	private void OnSetupStartupProgramMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
	{
		StartupHelper.SetupStartupProgram();
		UpdateSetupStartupProgramMenuFLyoutItemTextProperty();
	}

    private void OnSetupColorMenuFlyoutItemClicked(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
		var isWhiteIcon = Configuration.GetValue<bool>("WhiteIcon");
        Configuration.SetValue("WhiteIcon", !isWhiteIcon);
		isWhiteIcon = !isWhiteIcon;
        UpdateSetupColorMenuFlyoutItemTextProperty();

        var _imageFileName = (isWhiteIcon ? "Cpu_white.png" : "Cpu.png");
        iconImagePath = Path.Combine(assetsPath, _imageFileName);
        _iconImage = Image.FromFile(iconImagePath).GetThumbnailImage(TrayIconSize, TrayIconSize, null, IntPtr.Zero);

    }
}