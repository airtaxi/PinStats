using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinUIEx;

namespace PinStats.Helpers;

public static partial class WindowHelper
{
	private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	public enum DWM_WINDOW_CORNER_PREFERENCE
	{
		DWMWCP_DEFAULT = 0,
		DWMWCP_DONOTROUND = 1,
		DWMWCP_ROUND = 2,
		DWMWCP_ROUNDSMALL = 3
	}

	[LibraryImport("dwmapi.dll")]
	private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

	public static void SetWindowCornerToRoundedCorner(Window window)
	{
		IntPtr hwnd = window.GetWindowHandle();
		uint attribute = (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
		DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref attribute, sizeof(uint));
	}
}
