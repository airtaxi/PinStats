using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
namespace PinStats
{
	public partial class App : Application
	{
		private static Window s_tempWindow;

		public App()
		{
			InitializeComponent();
		}

		protected override void OnLaunched(LaunchActivatedEventArgs args)
		{
			s_tempWindow = new();
			s_tempWindow.AppWindow.IsShownInSwitchers = false;
			s_tempWindow.Activate();
			s_tempWindow.Hide();
		}
	}
}
