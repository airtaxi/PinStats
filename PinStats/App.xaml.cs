using H.NotifyIcon;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using System;
using System.IO;
using System.ComponentModel.DataAnnotations;
using PinStats.Helpers;

namespace PinStats;

public partial class App : Application
{
	private static Window s_tempWindow;

	public App()
	{
		Current.UnhandledException += OnApplicationUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

		InitializeComponent();
		StartupHelper.DummyMethod(); // Force static constructor to run.
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

		var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {exception?.Message ?? "UNKNOWN"}: {exception?.StackTrace ?? "UNKNOWN"}\n({baseDirectory})\n\n";
		File.AppendAllText(path, text);
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		// WinUI3 will exit when the last window is closed, so we need to create a dummy window to keep the app running.
		s_tempWindow = new();
		s_tempWindow.AppWindow.IsShownInSwitchers = false; // This window should not be shown in the taskbar.
		s_tempWindow.Activate();
		s_tempWindow.Hide(); // Hide the window so it doesn't appear on the screen.
	}
}
