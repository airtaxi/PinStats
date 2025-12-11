using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PinStats.Helpers;
using PinStats.Resources;
using System.Diagnostics;
using System.Reflection;

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

            var builder = new AppNotificationBuilder()
                .AddText(UpdateAvailableTitle)
                .AddText($"A new version ({remoteVersion}) is available.\nDo you want to download it?")
                .AddArgument("versionString", remoteVersionString);

            var notificationManager = AppNotificationManager.Default;
            notificationManager.Show(builder.BuildNotification());
        }
		catch (HttpRequestException) { } // Ignore 
		finally { UpdateCheckTimer.Change((int)TimeSpan.FromMinutes(UpdateCheckIntervalInMinutes).TotalMilliseconds, Timeout.Infinite); }
	}

	public App()
	{
		// Setup exception handlers to prevent the app from crashing and to log the exception.
		Application.Current.UnhandledException += OnApplicationUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

        AppNotificationManager notificationManager = AppNotificationManager.Default;
        notificationManager.NotificationInvoked += OnNotificationManagerNotificationInvoked;
        notificationManager.Register();

        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var activationKind = activatedArgs.Kind;

        if (activationKind != ExtendedActivationKind.AppNotification) LaunchEmptyWindowIfNotExists();
        else HandleNotification((AppNotificationActivatedEventArgs)activatedArgs.Data);

        if (RequestedTheme == ApplicationTheme.Light) LiveCharts.Configure(config => config.AddLightTheme());
        else LiveCharts.Configure(config => config.AddDarkTheme());

        InitializeComponent();
		InitializeThemeSettings();
		StartupHelper.DummyMethod(); // Force static constructor to run.
    }

    private static void OnNotificationManagerNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => HandleNotification(args);

    private static void HandleNotification(AppNotificationActivatedEventArgs args)
    {
        var versionString = args.Arguments["versionString"];
        if (versionString != null)
        {
            var url = "https://github.com/airtaxi/PinStats/releases/tag/" + versionString;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static void InitializeThemeSettings()
	{
		var hasThemeSettingsApplied = Configuration.GetValue<bool?>("WhiteIcon") != null;
		if (hasThemeSettingsApplied) return;

		var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
		Configuration.SetValue("WhiteIcon", isDarkTheme);
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

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		base.OnLaunched(args);
		LaunchEmptyWindowIfNotExists();
    }

	private static Window s_emptyWindow;
	// WinUI3 will exit when the last window is closed, so we need to create a dummy window to keep the app running.
	private void LaunchEmptyWindowIfNotExists()
	{
		if (s_emptyWindow != null) return;

		s_emptyWindow = new() { Content = new Frame() };

        // TaskbarUsageResources depends XamlRoot of the window, so we need to wait until the window is loaded.
        (s_emptyWindow.Content as Frame).Loaded += OnEmptyWindowContentLoaded;
        (s_emptyWindow.Content as Frame).ActualThemeChanged += OnActualThemeChanged;
        s_emptyWindow.AppWindow.IsShownInSwitchers = false; // This window should not be shown in the Taskbar.
        s_emptyWindow.Activate();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (RequestedTheme == ApplicationTheme.Light) LiveCharts.Configure(config => config.AddLightTheme());
        else LiveCharts.Configure(config => config.AddDarkTheme());
    }

    public static float MainWindowRasterizationScale { get; private set; }
    private async void OnEmptyWindowContentLoaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe the event to prevent memory leak.
        (s_emptyWindow.Content as Frame).Loaded -= OnEmptyWindowContentLoaded;

        // Hide the window so it doesn't appear on the screen.
        s_emptyWindow.Hide(false); // Hide the window so it doesn't appear on the screen.

        var xamlRoot = s_emptyWindow.Content.XamlRoot;
        MainWindowRasterizationScale = (float)xamlRoot.RasterizationScale;

        var resource = new TaskbarUsageResource();
        Resources.Add("TaskbarUsageResources", resource);
        await CheckForUpdateAsync();

        // Disable efficiency mode to improve performance.
        EfficiencyModeUtilities.SetEfficiencyMode(false);

        // Set the process priority to high to improve performance.
        Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
    }
}
