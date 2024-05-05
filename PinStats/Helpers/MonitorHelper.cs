using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;
using WinUIEx;
using static PinStats.Helpers.MonitorHelper;

namespace PinStats.Helpers;

#pragma warning disable IDE0044
#pragma warning disable CA1069
#pragma warning disable SYSLIB1054
public static class MonitorHelper
{
	public const int ERROR_SUCCESS = 0;

	#region enums

	public enum QUERY_DEVICE_CONFIG_FLAGS : uint
	{
		QDC_ALL_PATHS = 0x00000001,
		QDC_ONLY_ACTIVE_PATHS = 0x00000002,
		QDC_DATABASE_CURRENT = 0x00000004
	}

	public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
	{
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFF,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32 = 0xFFFFFFFF
	}

	public enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
	{
		DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
		DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3,
		DISPLAYCONFIG_SCANLINE_ORDERING_FORCE_UINT32 = 0xFFFFFFFF
	}

	public enum DISPLAYCONFIG_ROTATION : uint
	{
		DISPLAYCONFIG_ROTATION_IDENTITY = 1,
		DISPLAYCONFIG_ROTATION_ROTATE90 = 2,
		DISPLAYCONFIG_ROTATION_ROTATE180 = 3,
		DISPLAYCONFIG_ROTATION_ROTATE270 = 4,
		DISPLAYCONFIG_ROTATION_FORCE_UINT32 = 0xFFFFFFFF
	}

	public enum DISPLAYCONFIG_SCALING : uint
	{
		DISPLAYCONFIG_SCALING_IDENTITY = 1,
		DISPLAYCONFIG_SCALING_CENTERED = 2,
		DISPLAYCONFIG_SCALING_STRETCHED = 3,
		DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4,
		DISPLAYCONFIG_SCALING_CUSTOM = 5,
		DISPLAYCONFIG_SCALING_PREFERRED = 128,
		DISPLAYCONFIG_SCALING_FORCE_UINT32 = 0xFFFFFFFF
	}

	public enum DISPLAYCONFIG_PIXELFORMAT : uint
	{
		DISPLAYCONFIG_PIXELFORMAT_8BPP = 1,
		DISPLAYCONFIG_PIXELFORMAT_16BPP = 2,
		DISPLAYCONFIG_PIXELFORMAT_24BPP = 3,
		DISPLAYCONFIG_PIXELFORMAT_32BPP = 4,
		DISPLAYCONFIG_PIXELFORMAT_NONGDI = 5,
		DISPLAYCONFIG_PIXELFORMAT_FORCE_UINT32 = 0xffffffff
	}

	public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
	{
		DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
		DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
		DISPLAYCONFIG_MODE_INFO_TYPE_FORCE_UINT32 = 0xFFFFFFFF
	}

	public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
	{
		DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
		DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
		DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
		DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6,
		DISPLAYCONFIG_DEVICE_INFO_FORCE_UINT32 = 0xFFFFFFFF
	}

	#endregion

	#region structs

	[StructLayout(LayoutKind.Sequential)]
	public struct LUID
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_PATH_TARGET_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		private DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
		private DISPLAYCONFIG_ROTATION rotation;
		private DISPLAYCONFIG_SCALING scaling;
		private DISPLAYCONFIG_RATIONAL refreshRate;
		private DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
		public bool targetAvailable;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;
		public uint Denominator;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
		public uint flags;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_2DREGION
	{
		public uint cx;
		public uint cy;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
	{
		public ulong pixelRate;
		public DISPLAYCONFIG_RATIONAL hSyncFreq;
		public DISPLAYCONFIG_RATIONAL vSyncFreq;
		public DISPLAYCONFIG_2DREGION activeSize;
		public DISPLAYCONFIG_2DREGION totalSize;
		public uint videoStandard;
		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_TARGET_MODE
	{
		public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct POINTL
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_SOURCE_MODE
	{
		public uint width;
		public uint height;
		public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
		public POINTL position;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct DISPLAYCONFIG_MODE_INFO_UNION
	{
		[FieldOffset(0)]
		public DISPLAYCONFIG_TARGET_MODE targetMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_SOURCE_MODE sourceMode;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_MODE_INFO
	{
		public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
		public uint id;
		public LUID adapterId;
		public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
	{
		public uint value;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
		public uint size;
		public LUID adapterId;
		public uint id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
		public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
		public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
		public ushort edidManufactureId;
		public ushort edidProductCodeId;
		public uint connectorInstance;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string monitorFriendlyDeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string monitorDevicePath;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	#endregion

	#region DLL-Imports

	[DllImport("user32.dll")]
	private static extern int GetDisplayConfigBufferSizes(
		QUERY_DEVICE_CONFIG_FLAGS flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

	[DllImport("user32.dll")]
	private static extern int QueryDisplayConfig(
		QUERY_DEVICE_CONFIG_FLAGS flags,
		ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] PathInfoArray,
		ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] ModeInfoArray,
		IntPtr currentTopologyId
		);

	[DllImport("user32.dll")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref Win32Monitor lpmi);

	#endregion

	private static string MonitorFriendlyName(LUID adapterId, uint targetId)
	{
		var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
		{
			header =
				{
					size = (uint)Marshal.SizeOf(typeof (DISPLAYCONFIG_TARGET_DEVICE_NAME)),
					adapterId = adapterId,
					id = targetId,
					type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
				}
		};
		var error = DisplayConfigGetDeviceInfo(ref deviceName);
		if (error != ERROR_SUCCESS)
			throw new Win32Exception(error);
		return deviceName.monitorFriendlyDeviceName;
	}

	public struct SizeAndPosition
	{
		public int X;
		public int Y;
		public int Width;
		public int Height;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private class Win32Monitor
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public int dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}

	public class Monitor
	{
		public string MonitorName;
		public SizeAndPosition SizeAndPosition;
	}

	public static IEnumerable<Monitor> GetMonitors()
	{
		uint pathCount, modeCount;
		var error = GetDisplayConfigBufferSizes(QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
		if (error != ERROR_SUCCESS) throw new Win32Exception(error);

		var displayPaths = new DISPLAYCONFIG_PATH_INFO[pathCount];
		var displayModes = new DISPLAYCONFIG_MODE_INFO[modeCount];

		error = QueryDisplayConfig(QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref pathCount, displayPaths, ref modeCount, displayModes, IntPtr.Zero);
		if (error != ERROR_SUCCESS) throw new Win32Exception(error);

		foreach(var path in displayPaths)
		{
			// Query source and retrieve monitor size and position
			var source = displayModes.FirstOrDefault(x => x.id == path.sourceInfo.id && x.adapterId.HighPart == path.sourceInfo.adapterId.HighPart && x.adapterId.LowPart == path.sourceInfo.adapterId.LowPart);
			var sizeAndPosition = new SizeAndPosition
			{
				X = source.modeInfo.sourceMode.position.x,
				Y = source.modeInfo.sourceMode.position.y,
				Width = (int)source.modeInfo.sourceMode.width,
				Height = (int)source.modeInfo.sourceMode.height
			};

			// Query target and retrieve monitor friendly name
			var target = displayModes.FirstOrDefault(x => x.id == path.targetInfo.id);
			var monitorFriendlyName = MonitorFriendlyName(target.adapterId, target.id);

			// Empty monitor friendly name means internal display
			if (string.IsNullOrEmpty(monitorFriendlyName)) monitorFriendlyName = "Internal Display";

			// Synthesize monitor object
			var monitor = new Monitor
			{
				MonitorName = monitorFriendlyName,
				SizeAndPosition = sizeAndPosition
			};
			yield return monitor;
		}
	}

	public static void PositionWindowToMonitor(IntPtr hWnd, Monitor monitor)
	{
		var sizeAndPosition = monitor.SizeAndPosition;
		SetWindowPos(hWnd, IntPtr.Zero, sizeAndPosition.X, sizeAndPosition.Y, sizeAndPosition.Width, sizeAndPosition.Height, 0);
    }

    #region Display Scale
    [DllImport("gdi32.dll")]
    static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);
    const int LOGPIXELSX = 88;

    public static float GetMainMonitorDisplayScaleRatio()
    {
        IntPtr desktop = GetDC(IntPtr.Zero);
        int dpiX = GetDeviceCaps(desktop, LOGPIXELSX);
		var scale = dpiX / 96f;
		return scale;
    }
    #endregion
}
#pragma warning restore IDE0044
#pragma warning restore CA1069
#pragma warning restore SYSLIB1054