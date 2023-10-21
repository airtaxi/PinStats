using H.NotifyIcon;
using Microsoft.UI.Xaml;
using PinStats.Helpers;
using PinStats.Resources;
using System.Reflection;
using Windows.UI.Notifications;
using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;
using HarfBuzzSharp;

namespace PinStats;

public partial class App : Application
{
	private const int UpdateCheckIntervalInMinutes = 10;
	private static readonly Timer UpdateCheckTimer;

	static App()
	{
		UpdateCheckTimer = new(UpdateCheckTimerCallback, null, (int)TimeSpan.FromMinutes(UpdateCheckIntervalInMinutes).TotalMilliseconds, Timeout.Infinite);
	}

	private const string UpdateAvailableTitle = "Update Available";
	private static async void UpdateCheckTimerCallback(object state) => await CheckForUpdateAsync();

	private static async Task CheckForUpdateAsync()
	{
		try
		{
			var url = "https://raw.githubusercontent.com/airtaxi/PinStats/master/latest";
			var remoteVersionString = await HttpHelper.GetContentFromUrlAsync(url);
			if (remoteVersionString is null) return;

			var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
			var remoteVersion = new Version(remoteVersionString);
			if (localVersion >= remoteVersion) return;

			var configurationKey = "versionChecked" + remoteVersionString;
			var hasNotificationShownForRemoteVersion = Configuration.GetValue<bool?>(configurationKey) ?? false;
			if (hasNotificationShownForRemoteVersion) return;
			Configuration.SetValue(configurationKey, true);

			var builder = new ToastContentBuilder()
			.AddText(UpdateAvailableTitle)
			.AddText($"A new version ({remoteVersion}) is available.\nDo you want to download it?")
			.AddArgument("versionString", remoteVersionString);
			builder.Show();
		}
		finally { UpdateCheckTimer.Change((int)TimeSpan.FromMinutes(UpdateCheckIntervalInMinutes).TotalMilliseconds, Timeout.Infinite); }
	}

	public App()
	{
		// Setup exception handlers to prevent the app from crashing and to log the exception.
		Application.Current.UnhandledException += OnApplicationUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
		ToastNotificationManagerCompat.OnActivated += OnToastNotificationActivated;

		InitializeComponent();
		InitializeThemeSettings();
		StartupHelper.DummyMethod(); // Force static constructor to run.
	}

	private static void InitializeThemeSettings()
	{
		var hasThemeSettingsApplied = Configuration.GetValue<bool?>("WhiteIcon") != null;
		if (hasThemeSettingsApplied) return;

		var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
		Configuration.SetValue("WhiteIcon", isDarkTheme);
	}

	private static void OnToastNotificationActivated(ToastNotificationActivatedEventArgsCompat toastArgs)
	{
		ToastArguments args = ToastArguments.Parse(toastArgs.Argument);
		var versionString = args["versionString"];
		if(versionString != null)
		{
			var url = "https://github.com/airtaxi/PinStats/releases/tag/" + versionString;
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
	}

	private void OnTaskSchedulerUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) => WriteException(e.Exception);
	private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e) => WriteException(e.ExceptionObject as Exception);
	private void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		e.Handled = true;
		WriteException(e.Exception);
	}

	private static void WriteException(Exception exception)
	{
		var baseDirectory = AppContext.BaseDirectory;
		var path = Path.Combine(baseDirectory, "error.log");

		if (exception is null)
		{
			File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] UNKNOWN\n({baseDirectory})\n\n");
			return;
		}

		var exceptionName = exception.GetType().Name;

		var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ({exceptionName}) {exception?.Message ?? "UNKNOWN"}: {exception?.StackTrace ?? "UNKNOWN"}\n({baseDirectory})\n\n";
		File.AppendAllText(path, text);

		if (exception.InnerException is not null) WriteException(exception.InnerException);
	}

	protected override async void OnLaunched(LaunchActivatedEventArgs args)
	{
		base.OnLaunched(args);
		var resource = new TaskbarUsageResources();
		Resources.Add("TaskbarUsageResources", resource);
		LaunchDummyWindowIfNotExists();
		await CheckForUpdateAsync();
	}

	private static Window s_tempWindow;
	public static void LaunchDummyWindowIfNotExists()
	{
		if (s_tempWindow is not null) return;
		// WinUI3 will exit when the last window is closed, so we need to create a dummy window to keep the app running.
		s_tempWindow = new();
		s_tempWindow.AppWindow.IsShownInSwitchers = false; // This window should not be shown in the Taskbar.
		s_tempWindow.Activate();
		s_tempWindow.Hide(); // Hide the window so it doesn't appear on the screen.
	}
}
