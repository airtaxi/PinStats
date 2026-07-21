using Deskband11Lib.WinUI;
using Microsoft.UI.Xaml;
using System.Runtime.Versioning;

namespace PinStats.Views;

[SupportedOSPlatform("windows10.0.22000.0")]
public sealed partial class TaskbarWidgetWindow : Window
{
	public TaskbarContentHost TaskbarContentHost { get; }

	public TaskbarWidgetWindow()
	{
		InitializeComponent();
		TaskbarContentHost = new TaskbarContentHost(this, (FrameworkElement)Content, new() { PreferredWidth = TaskbarWidgetSettings.GetPreferredWidth(), PreferredMonitorIdentity = TaskbarWidgetSettings.PreferredMonitorIdentity });
	}

	public async Task PrepareTaskbarContentAsync() => await TaskbarContentHost.AttachWhenLayoutReadyAsync();

	private void OnTaskbarWidgetWindowClosed(object sender, WindowEventArgs args)
	{
		TaskbarContentHost.Dispose();
		TaskbarWidgetContent.Dispose();
	}
}
