using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.ViewModels;
using System;
using System.Runtime.InteropServices;
using WinUIEx;

namespace PinStats;

public sealed partial class ReportWindow
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

	public readonly static UsageViewModel CpuUsageViewModel = new();
	public ReportWindow()
	{
		InitializeComponent();

		SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
		AppWindow.IsShownInSwitchers = false;
		(AppWindow.Presenter as OverlappedPresenter).SetBorderAndTitleBar(true, false);

		// AppWindow that manually set the border and title bar is not rounded.
		IntPtr hwnd = this.GetWindowHandle();
		uint attribute = (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
		DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref attribute, sizeof(uint));

		Activated += OnActivated;
		CpuUsageViewModel.RefreshSync(); // Renew the "sync" of the CpuUsageViewModel to prevent the chart from not being properly displayed.
		CartesianChartCpuUsage.DataContext = CpuUsageViewModel;
	}

	private void OnActivated(object sender, WindowActivatedEventArgs args)
	{
		// Close the window when the window lost its focus.
		if (args.WindowActivationState == WindowActivationState.Deactivated) Close();
	}
}
