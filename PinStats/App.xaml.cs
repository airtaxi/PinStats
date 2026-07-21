using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PinStats.Helpers;
using PinStats.Services;
using PinStats.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

namespace PinStats;

public partial class App : Application
{
    private const int UpdateCheckIntervalInMinutes = 10;
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/airtaxi/PinStats/releases/latest";
    private const string GitHubReleaseTagUrl = "https://github.com/airtaxi/PinStats/releases/tag/";
    private const string GitHubApiUserAgent = "PinStats";

    private static readonly HttpClient UpdateCheckHttpClient = CreateUpdateCheckHttpClient();
    private static readonly Timer UpdateCheckTimer;

    public static IServiceProvider Services { get; private set; } = null!;
    public static LocalizationService Localization { get; private set; } = null!;

    static App()
    {
        UpdateCheckTimer = new(UpdateCheckTimerCallback, null, (int)TimeSpan.FromMinutes(UpdateCheckIntervalInMinutes).TotalMilliseconds, Timeout.Infinite);
    }

    private static async void UpdateCheckTimerCallback(object state) => await CheckForUpdateAsync();

    private static HttpClient CreateUpdateCheckHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubApiUserAgent);
        return httpClient;
    }

    private static async Task CheckForUpdateAsync()
    {
        try
        {
            var remoteVersionTagName = await GetLatestReleaseTagNameAsync();
            if (string.IsNullOrWhiteSpace(remoteVersionTagName)) return;

            var remoteVersionString = remoteVersionTagName.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(remoteVersionString, out var remoteVersion)) return;

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (localVersion >= remoteVersion) return;

            var configurationKey = "versionChecked" + remoteVersionTagName;
            var hasNotificationShownForRemoteVersion = Configuration.GetValue<bool?>(configurationKey) ?? false;
            if (hasNotificationShownForRemoteVersion) return;
            Configuration.SetValue(configurationKey, true);

            var updateAvailableTitle = Localization.GetLocalizedString("Update.Title");
            var updateAvailableMessage = Localization.GetFormattedString("Update.MessageFormat", remoteVersion);

            var builder = new AppNotificationBuilder()
                .AddText(updateAvailableTitle)
                .AddText(updateAvailableMessage)
                .AddArgument("versionString", remoteVersionTagName);

            var notificationManager = AppNotificationManager.Default;
            notificationManager.Show(builder.BuildNotification());
        }
        catch (HttpRequestException) { } // Ignore
        catch (TaskCanceledException) { } // Ignore
        catch (JsonException) { } // Ignore
        finally { UpdateCheckTimer.Change((int)TimeSpan.FromMinutes(UpdateCheckIntervalInMinutes).TotalMilliseconds, Timeout.Infinite); }
    }

    private static async Task<string> GetLatestReleaseTagNameAsync()
    {
        var json = await UpdateCheckHttpClient.GetStringAsync(GitHubLatestReleaseApiUrl);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("tag_name", out var tagNameElement) ? tagNameElement.GetString() : null;
    }

    public App()
    {
        // Setup dependency injection container.
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();
        Localization = Services.GetRequiredService<LocalizationService>();

        // Setup exception handlers to prevent the app from crashing and to log the exception.
        Application.Current.UnhandledException += OnApplicationUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

#if !DEBUG
		var notificationManager = AppNotificationManager.Default;
		notificationManager.NotificationInvoked += OnNotificationManagerNotificationInvoked;
		notificationManager.Register();

		var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
		var activationKind = activatedArgs.Kind;

		if (activationKind == ExtendedActivationKind.AppNotification) HandleNotification((AppNotificationActivatedEventArgs)activatedArgs.Data);
#endif

        if (RequestedTheme == ApplicationTheme.Light) LiveCharts.Configure(config => config.AddLightTheme());
        else LiveCharts.Configure(config => config.AddDarkTheme());

        InitializeComponent();
        StartupHelper.DummyMethod(); // Force static constructor to run.
    }


#if !DEBUG
	private static void OnNotificationManagerNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => HandleNotification(args);

	private static void HandleNotification(AppNotificationActivatedEventArgs args)
	{
		var versionString = args.Arguments["versionString"];
		if (versionString != null)
		{
			var url = GitHubReleaseTagUrl + versionString;
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
	}
#endif

    private static void InitializeThemeSettings()
    {
        var hasThemeSettingsApplied = Configuration.GetValue<bool?>("WhiteIcon") != null;
        if (hasThemeSettingsApplied) return;

        var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        Configuration.SetValue("WhiteIcon", isDarkTheme);
    }

    private static void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<LocalizationService>(_ => new LocalizationService(Configuration.GetValue<string>("LanguageOverride") ?? string.Empty));
        serviceCollection.AddSingleton<SystemThemeService>();
    }

    private void OnTaskSchedulerUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) => WriteException(e.Exception);
    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e) => WriteException(e.ExceptionObject as Exception);
    private void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        WriteException(e.Exception);
    }

    public static void WriteException(Exception exception)
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
        LaunchTaskbarIconHostWindowIfNotExists();
    }

    private static TaskbarIconHostWindow s_iconHostWindow;
    // WinUI3 will exit when the last window is closed, so we need to create a host window to keep the app running.
    // The host window is hidden and hosts the H.NotifyIcon TaskbarIcon that would otherwise live in a ResourceDictionary.
    private void LaunchTaskbarIconHostWindowIfNotExists()
    {
        if (s_iconHostWindow != null) return;

        s_iconHostWindow = new TaskbarIconHostWindow();

        // TaskbarIcon depends on XamlRoot of the window, so we need to wait until the content is loaded.
        (s_iconHostWindow.Content as FrameworkElement).Loaded += OnTaskbarIconHostContentLoaded;
        (s_iconHostWindow.Content as FrameworkElement).ActualThemeChanged += OnActualThemeChanged;
        s_iconHostWindow.AppWindow.IsShownInSwitchers = false; // This window should not be shown in the Taskbar.
        s_iconHostWindow.Activate();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (RequestedTheme == ApplicationTheme.Light) LiveCharts.Configure(config => config.AddLightTheme());
        else LiveCharts.Configure(config => config.AddDarkTheme());
    }

    public static float MainWindowRasterizationScale { get; private set; }
    private async void OnTaskbarIconHostContentLoaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe the event to prevent memory leak.
        (s_iconHostWindow.Content as FrameworkElement).Loaded -= OnTaskbarIconHostContentLoaded;

        s_iconHostWindow.Hide(false); // Hide the window so it doesn't appear on the screen.

        var xamlRoot = s_iconHostWindow.Content.XamlRoot;
        MainWindowRasterizationScale = (float)xamlRoot.RasterizationScale;

        await CheckForUpdateAsync();

        // Attach the taskbar widget window if any widget items are enabled.
        try { await InitializeTaskbarWidgetWindowAsync(); }
        catch (Exception exception) { WriteException(exception); }

        // Disable efficiency mode to improve performance.
        EfficiencyModeUtilities.SetEfficiencyMode(false);

        // Set the process priority to high to improve performance.
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
    }

    private static TaskbarWidgetWindow s_taskbarWidgetWindow;

    public static async Task InitializeTaskbarWidgetWindowAsync()
    {
        // The taskbar widget is only supported on Windows 11 and later.
        if (!TaskbarHelper.IsWindows11OrGreater()) return;
        if (!TaskbarWidgetSettings.HasAnyItemEnabled) return;
        if (s_taskbarWidgetWindow is not null) return;

        var taskbarWidgetWindow = new TaskbarWidgetWindow();
        s_taskbarWidgetWindow = taskbarWidgetWindow;
        taskbarWidgetWindow.Closed += OnTaskbarWidgetWindowClosed;
        taskbarWidgetWindow.TaskbarContentHost.TaskbarWindowRecreated += OnTaskbarContentHostTaskbarWindowRecreated;

        try
        {
            await taskbarWidgetWindow.PrepareTaskbarContentAsync();
            taskbarWidgetWindow.Activate();
        }
        catch
        {
            ReleaseTaskbarWidgetWindow(taskbarWidgetWindow);
            taskbarWidgetWindow.Close();
            throw;
        }
    }

    // Recreate the taskbar widget window to apply configuration changes such as enabled items or the preferred monitor.
    // The old window must be closed after the new window is ready since closing it first can terminate the app if it is the last remaining window.
    public static async void RelaunchTaskbarWidgetWindow()
    {
        if (!TaskbarHelper.IsWindows11OrGreater()) return;

        var oldTaskbarWidgetWindow = s_taskbarWidgetWindow;
        if (oldTaskbarWidgetWindow is not null) ReleaseTaskbarWidgetWindow(oldTaskbarWidgetWindow);

        try { await InitializeTaskbarWidgetWindowAsync(); }
        catch (Exception exception) { WriteException(exception); }
        finally { oldTaskbarWidgetWindow?.Close(); }
    }

    private static void OnTaskbarWidgetWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is TaskbarWidgetWindow taskbarWidgetWindow)
        {
            ReleaseTaskbarWidgetWindow(taskbarWidgetWindow);
        }
    }

    // The taskbar window is recreated when Explorer restarts, so the widget window must be recreated to attach to it again.
    private static async void OnTaskbarContentHostTaskbarWindowRecreated(object sender, EventArgs args)
    {
        if (!TaskbarHelper.IsWindows11OrGreater()) return;

        var taskbarWidgetWindow = s_taskbarWidgetWindow;
        if (taskbarWidgetWindow is not null) ReleaseTaskbarWidgetWindow(taskbarWidgetWindow);

        try
        {
            // Wait for the taskbar to be ready before recreating the widget window.
            await Task.Delay(1000);
            await InitializeTaskbarWidgetWindowAsync();
        }
        catch (Exception exception) { WriteException(exception); }
    }

    private static void ReleaseTaskbarWidgetWindow(TaskbarWidgetWindow taskbarWidgetWindow)
    {
        taskbarWidgetWindow.Closed -= OnTaskbarWidgetWindowClosed;
        if (TaskbarHelper.IsWindows11OrGreater()) taskbarWidgetWindow.TaskbarContentHost.TaskbarWindowRecreated -= OnTaskbarContentHostTaskbarWindowRecreated;
        if (ReferenceEquals(s_taskbarWidgetWindow, taskbarWidgetWindow)) s_taskbarWidgetWindow = null;
    }
}
