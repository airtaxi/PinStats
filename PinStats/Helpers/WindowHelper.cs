using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinUIEx;

namespace PinStats.Helpers;

public static partial class WindowHelper
{
	private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public enum DWM_WINDOW_CORNER_PREFERENCE
	{
		DWMWCP_DEFAULT = 0,
		DWMWCP_DONOTROUND = 1,
		DWMWCP_ROUND = 2,
		DWMWCP_ROUNDSMALL = 3
    }

    // Import the necessary function from user32.dll
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [LibraryImport("dwmapi.dll")]
	private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

	public static void SetWindowCornerToRoundedCorner(Window window)
	{
		IntPtr hwnd = window.GetWindowHandle();
		uint attribute = (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
		DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref attribute, sizeof(uint));
    }

    public static void DisableWindowAnimations(Window window)
    {
        IntPtr hwnd = window.GetWindowHandle();
        uint disableAnimation = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disableAnimation, sizeof(uint));
    }

    public static void SetDarkModeWindow(Window window)
    {
        IntPtr hwnd = window.GetWindowHandle();
        uint darkMode = 1;

        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(uint));
    }

}
