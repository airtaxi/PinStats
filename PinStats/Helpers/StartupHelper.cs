using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System.Diagnostics;

namespace PinStats.Helpers;

public static class StartupHelper
{
	private const string ProgramName = "PinStats";
	private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

	// Delete registry key value that was created by the previous version of the program.
	static StartupHelper()
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
		var hasValue = key.GetValue(ProgramName) != null;
		if (hasValue)
		{
			key.DeleteValue(ProgramName, false);
			SetupStartupProgram(); // Reinitialize startup program because the program now uses Task Scheduler instead of registry key.
		}
	}

	public static bool IsStartupProgram
	{
		get
		{
			using var taskService = new TaskService();
			return taskService.FindTask(ProgramName) != null;
		}
	}

	public static void SetupStartupProgram()
	{
		using var taskService = new TaskService();

		if(IsStartupProgram) taskService.RootFolder.DeleteTask(ProgramName);
		else
		{
			using var taskDefinition = taskService.NewTask();
			taskDefinition.RegistrationInfo.Description = "Set up " + ProgramName + " to run at startup.";
			taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
		
			taskDefinition.Triggers.Add(new LogonTrigger());
			taskDefinition.Settings.DisallowStartIfOnBatteries = false;
			taskDefinition.Settings.StopIfGoingOnBatteries = false;
			taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;

			var programPath = Process.GetCurrentProcess().MainModule.FileName;
			taskDefinition.Actions.Add(new ExecAction(programPath, null, null));

			taskService.RootFolder.RegisterTaskDefinition(ProgramName, taskDefinition);
		}
	}

	public static void DummyMethod()
	{
		// Dummy method to force static constructor to run.
	}
}
