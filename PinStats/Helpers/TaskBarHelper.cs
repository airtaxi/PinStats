using PinStats.Enums;
using System;
using System.Runtime.InteropServices;

namespace PinStats.Helpers;

public static partial class TaskBarHelper
{
	private const int ABM_GETTASKBARPOS = 5;

	[LibraryImport("shell32.dll")]
	private static partial IntPtr SHAppBarMessage(int msg, ref APPBARDATA data);

	private struct APPBARDATA
	{
		public int cbSize;
		public IntPtr hWnd;
		public int uCallbackMessage;
		public int uEdge;
		public RECT rc;
		public IntPtr lParam;
	}

	public struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	public static RECT GetTaskBarRect()
	{
		var appBarData = GetAppbarData();
		return appBarData.rc;
	}

	private static APPBARDATA GetAppbarData()
	{
		var data = new APPBARDATA() { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
		SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
		return data;
	}

	public static TaskBarPosition GetTaskBarPosition()
	{
		// Windows 11 is currently not support to change the task bar position. fallback to bottom.
		if (IsWindows11OrGreater()) return TaskBarPosition.Bottom;

		var appBarData = GetAppbarData();
		return appBarData.uEdge switch
		{
			0 => TaskBarPosition.Left,
			1 => TaskBarPosition.Top,
			2 => TaskBarPosition.Right,
			3 => TaskBarPosition.Bottom,
			_ => TaskBarPosition.Bottom,// default value
		};
	}

	public static bool IsWindows11OrGreater()
	{
		OperatingSystem os = Environment.OSVersion;
		Version version = os.Version;

		// Windows 11 has at least major version 10 and build version 22000.
		return version.Major >= 10 && version.Build >= 22000;
	}

}
