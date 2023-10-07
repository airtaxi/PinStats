using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.ViewModels;
using System;
using System.Runtime.InteropServices;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PinStats;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
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

	public readonly static CpuUsageViewModel CpuUsageViewModel = new();

	public ReportWindow()
	{
		InitializeComponent();

		SystemBackdrop = new MicaBackdrop() { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
		AppWindow.IsShownInSwitchers = false;
		(AppWindow.Presenter as OverlappedPresenter).SetBorderAndTitleBar(true, false);

		IntPtr hwnd = this.GetWindowHandle();
		uint attribute = (uint)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
		DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref attribute, sizeof(uint));

		Activated += OnActivated;
		CpuUsageViewModel.InitializeSync();
		CartesianChartCpuUsage.DataContext = CpuUsageViewModel;
	}

	private void OnActivated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState == WindowActivationState.Deactivated) Close();
	}
}
