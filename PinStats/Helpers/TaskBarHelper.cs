using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PinStats.Helpers
{
	public static class TaskBarHelper
	{
		private const int ABM_GETTASKBARPOS = 5;

		[System.Runtime.InteropServices.DllImport("shell32.dll")]
		private static extern IntPtr SHAppBarMessage(int msg, ref APPBARDATA data);

		private struct APPBARDATA
		{
			public int cbSize;
			public IntPtr hWnd;
			public int uCallbackMessage;
			public int uEdge;
			public RECT rc;
			public IntPtr lParam;
		}

		private struct RECT
		{
			public int left, top, right, bottom;
		}

		public static int GetTaskBarTop()
		{
			APPBARDATA data = new APPBARDATA();
			data.cbSize = Marshal.SizeOf(data);
			SHAppBarMessage(ABM_GETTASKBARPOS, ref data);

			return data.rc.top;
		}
	}
}
