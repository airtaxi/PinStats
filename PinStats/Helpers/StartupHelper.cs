using Microsoft.Win32;
using System.Diagnostics;

namespace PinStats.Helpers;

public static class StartupHelper
{
	private const string ProgramName = "PinStats";
	private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

	public static bool IsStartupProgram {
		get
		{
			using RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
			return key.GetValue(ProgramName) != null;
		}
	}

	public static void SetupStartupProgram()
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);

		if (!IsStartupProgram) key.SetValue(ProgramName, Process.GetCurrentProcess().MainModule.FileName);
		else key.DeleteValue(ProgramName, false);

		key.Close();
	}
}
