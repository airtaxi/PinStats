using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Power;

namespace PinStats.Helpers;

/// <summary>
/// Reads the battery charge/discharge rate directly from the battery device driver via IOCTL.
/// A failed IOCTL triggers tag re-query and device reopen instead of re-reporting the last value,
/// so the battery values never go stale.
/// </summary>
public static partial class BatteryStatusHelper
{
	private static readonly object s_devicesLock = new();
	private static List<BatteryDevice> s_devices;
	private static bool s_initializationAttempted;
	private static bool s_batteryEverSeen;

	/// <summary>
	/// Returns the total battery charge rate in watts. Positive means charging, negative means discharging.
	/// Returns null when there is no battery or the rate is unavailable.
	/// </summary>
	public static float? GetTotalBatteryChargeRateInWatts()
	{
		lock (s_devicesLock)
		{
			EnsureDevicesInitialized();
			if (s_devices.Count == 0) return null;

			var totalRateInWatts = 0f;
			var anyRateRead = false;
			var failedDevices = new List<BatteryDevice>();

			foreach (var device in s_devices)
			{
				if (TryReadDeviceRate(device, out var rateInWatts))
				{
					if (rateInWatts.HasValue)
					{
						totalRateInWatts += rateInWatts.Value;
						anyRateRead = true;
					}
				}
				else failedDevices.Add(device);
			}

			// Transient failures (e.g., after sleep/resume) trigger re-enumeration
			// on the next poll, so battery hardware is never permanently removed.
			HandleQueryFailures(failedDevices);

			return anyRateRead ? totalRateInWatts : null;
		}
	}

	/// <summary>
	/// Returns the total battery percentage across all batteries.
	/// Returns null when there is no battery or the percentage is unavailable.
	/// </summary>
	public static float? GetTotalBatteryPercent()
	{
		lock (s_devicesLock)
		{
			EnsureDevicesInitialized();
			if (s_devices.Count == 0) return null;

			var remainingCapacityInMilliwattHours = 0f;
			var fullyChargedCapacityInMilliwattHours = 0f;
			var anyCapacityRead = false;
			var failedDevices = new List<BatteryDevice>();

			foreach (var device in s_devices)
			{
				if (TryQueryStatusWithRecovery(device, out var batteryStatus))
				{
					if (batteryStatus.Capacity != PInvoke.BATTERY_UNKNOWN_CAPACITY)
					{
						remainingCapacityInMilliwattHours += batteryStatus.Capacity;
						anyCapacityRead = true;
					}

					if (device.FullChargedCapacity != PInvoke.BATTERY_UNKNOWN_CAPACITY)
						fullyChargedCapacityInMilliwattHours += device.FullChargedCapacity;
				}
				else failedDevices.Add(device);
			}

			HandleQueryFailures(failedDevices);

			if (!anyCapacityRead || fullyChargedCapacityInMilliwattHours == 0) return null;
			return remainingCapacityInMilliwattHours / fullyChargedCapacityInMilliwattHours * 100;
		}
	}

	/// <summary>
	/// Returns the total estimated battery life time across all batteries.
	/// Returns null when there is no battery or the time is unavailable.
	/// </summary>
	public static TimeSpan? GetTotalBatteryEstimatedTime()
	{
		lock (s_devicesLock)
		{
			EnsureDevicesInitialized();
			if (s_devices.Count == 0) return null;

			var totalEstimatedTimeInSeconds = 0L;
			var anyTimeRead = false;
			var failedDevices = new List<BatteryDevice>();

			foreach (var device in s_devices)
			{
				if (TryQueryEstimatedTimeWithRecovery(device, out var estimatedTimeInSeconds))
				{
					if (estimatedTimeInSeconds.HasValue)
					{
						totalEstimatedTimeInSeconds += estimatedTimeInSeconds.Value;
						anyTimeRead = true;
					}
				}
				else failedDevices.Add(device);
			}

			HandleQueryFailures(failedDevices);

			if (!anyTimeRead || totalEstimatedTimeInSeconds == 0) return null;
			return TimeSpan.FromSeconds(totalEstimatedTimeInSeconds);
		}
	}

	/// <summary>
	/// Returns the average battery health percent across all batteries.
	/// Returns null when there is no battery or the health is unavailable.
	/// </summary>
	public static float? GetAverageBatteryHealthPercent()
	{
		lock (s_devicesLock)
		{
			EnsureDevicesInitialized();
			if (s_devices.Count == 0) return null;

			var designedCapacityInMilliwattHours = 0f;
			var fullyChargedCapacityInMilliwattHours = 0f;

			foreach (var device in s_devices)
			{
				if (device.DesignedCapacity != PInvoke.BATTERY_UNKNOWN_CAPACITY)
					designedCapacityInMilliwattHours += device.DesignedCapacity;
				if (device.FullChargedCapacity != PInvoke.BATTERY_UNKNOWN_CAPACITY)
					fullyChargedCapacityInMilliwattHours += device.FullChargedCapacity;
			}

			if (designedCapacityInMilliwattHours == 0 || fullyChargedCapacityInMilliwattHours == 0) return null;
			return fullyChargedCapacityInMilliwattHours / designedCapacityInMilliwattHours * 100;
		}
	}

	private static void HandleQueryFailures(List<BatteryDevice> failedDevices)
	{
		if (failedDevices.Count == 0) return;

		// A transient IOCTL failure must not permanently remove battery hardware.
		// Dispose the current devices and re-enumerate them on the next poll.
		foreach (var device in s_devices)
		{
			device.Handle.Dispose();
		}

		s_devices = [];
		s_initializationAttempted = false;
	}

	private static void EnsureDevicesInitialized()
	{
		if (s_initializationAttempted) return;
		s_initializationAttempted = true;
		s_devices = EnumerateBatteryDevices();

		// A re-enumeration may come up empty while the battery is transiently absent
		// (e.g., right after sleep/resume). If a battery existed before, keep retrying
		// so the battery is never permanently lost until the app restarts.
		if (s_devices.Count > 0) s_batteryEverSeen = true;
		else if (s_batteryEverSeen) s_initializationAttempted = false;
	}

	private static unsafe List<BatteryDevice> EnumerateBatteryDevices()
	{
		var devices = new List<BatteryDevice>();
		try
		{
			var hdev = PInvoke.SetupDiGetClassDevs(PInvoke.GUID_DEVICE_BATTERY, null, HWND.Null, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_DEVICEINTERFACE);
			if (hdev.IsInvalid) return devices;

			try
			{
				for (uint i = 0; ; i++)
				{
					var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA) };

					if (!PInvoke.SetupDiEnumDeviceInterfaces(hdev, null, PInvoke.GUID_DEVICE_BATTERY, i, ref data))
					{
						if (Marshal.GetLastWin32Error() == (int)WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
						continue;
					}

					uint cbRequired = 0;
					PInvoke.SetupDiGetDeviceInterfaceDetail((HDEVINFO)hdev.DangerousGetHandle(), &data, null, 0, &cbRequired, null);
					if (Marshal.GetLastWin32Error() != (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER) continue;

					var buffer = Marshal.AllocHGlobal((int)cbRequired);
					try
					{
						var pDetailData = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)buffer;
						pDetailData->cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);

						if (!PInvoke.SetupDiGetDeviceInterfaceDetail((HDEVINFO)hdev.DangerousGetHandle(), &data, pDetailData, cbRequired, &cbRequired, null)) continue;

						string devicePath;
						fixed (void* pDevicePath = &pDetailData->DevicePath.e0)
							devicePath = new string((char*)pDevicePath);

						var battery = PInvoke.CreateFile(devicePath, (uint)FileAccess.ReadWrite, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
						if (battery.IsInvalid) continue;

						var bqi = default(BATTERY_QUERY_INFORMATION);
						var dwWait = 0u;
						var bytesReturned = 0u;
						if (!PInvoke.DeviceIoControl((HANDLE)battery.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_TAG, &dwWait, sizeof(uint), &bqi.BatteryTag, sizeof(uint), &bytesReturned, null))
						{
							battery.Dispose();
							continue;
						}

						var bi = default(BATTERY_INFORMATION);
						bqi.InformationLevel = BATTERY_QUERY_INFORMATION_LEVEL.BatteryInformation;
						if (!PInvoke.DeviceIoControl((HANDLE)battery.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_INFORMATION, &bqi, (uint)sizeof(BATTERY_QUERY_INFORMATION), &bi, (uint)sizeof(BATTERY_INFORMATION), &bytesReturned, null))
						{
							battery.Dispose();
							continue;
						}

						// Only system batteries are counted, excluding UPS devices.
						if ((bi.Capabilities & PInvoke.BATTERY_SYSTEM_BATTERY) == 0)
						{
							battery.Dispose();
							continue;
						}

						devices.Add(new BatteryDevice(devicePath, battery, bqi.BatteryTag, bi.DesignedCapacity, bi.FullChargedCapacity));
					}
					finally { Marshal.FreeHGlobal(buffer); }
				}
			}
			finally { hdev.Dispose(); }
		}
		catch (Exception exception) { App.WriteException(exception); }

		return devices;
	}

	private static bool TryReadDeviceRate(BatteryDevice device, out float? rateInWatts)
	{
		if (!TryQueryStatusWithRecovery(device, out var batteryStatus))
		{
			rateInWatts = null;
			return false;
		}

		if ((uint)batteryStatus.Rate == PInvoke.BATTERY_UNKNOWN_RATE)
		{
			rateInWatts = null;
			return true; // The device is valid, but the rate is not reported.
		}

		rateInWatts = batteryStatus.Rate / 1000f;
		return true;
	}

	private static bool TryQueryStatusWithRecovery(BatteryDevice device, out BATTERY_STATUS batteryStatus)
	{
		if (TryQueryStatus(device, out batteryStatus)) return true;

		// Windows invalidates the battery tag when battery characteristics change.
		// A failed status query usually means the cached tag is stale, so recover and retry once.
		if (TryRecoverDevice(device)) return TryQueryStatus(device, out batteryStatus);

		return false;
	}

	private static unsafe bool TryQueryStatus(BatteryDevice device, out BATTERY_STATUS batteryStatus)
	{
		batteryStatus = default;

		var bws = default(BATTERY_WAIT_STATUS);
		bws.BatteryTag = device.Tag;
		var status = default(BATTERY_STATUS);
		if (!PInvoke.DeviceIoControl((HANDLE)device.Handle.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_STATUS, &bws, (uint)sizeof(BATTERY_WAIT_STATUS), &status, (uint)sizeof(BATTERY_STATUS), null, null)) return false;

		batteryStatus = status;
		return true;
	}

	private static unsafe bool TryQueryEstimatedTime(BatteryDevice device, out uint estimatedTimeInSeconds)
	{
		estimatedTimeInSeconds = 0;

		var bqi = default(BATTERY_QUERY_INFORMATION);
		bqi.BatteryTag = device.Tag;
		bqi.InformationLevel = BATTERY_QUERY_INFORMATION_LEVEL.BatteryEstimatedTime;
		var seconds = 0u;
		if (!PInvoke.DeviceIoControl((HANDLE)device.Handle.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_INFORMATION, &bqi, (uint)sizeof(BATTERY_QUERY_INFORMATION), &seconds, sizeof(uint), null, null)) return false;

		estimatedTimeInSeconds = seconds;
		return true;
	}

	private static bool TryQueryEstimatedTimeWithRecovery(BatteryDevice device, out uint? estimatedTimeInSeconds)
	{
		estimatedTimeInSeconds = null;

		if (TryQueryEstimatedTime(device, out var seconds))
		{
			// The time is unknown while charging or on AC power, which is not a failure.
			if (seconds != PInvoke.BATTERY_UNKNOWN_TIME) estimatedTimeInSeconds = seconds;
			return true;
		}

		// The tag may be stale if the battery characteristics changed, so recover and retry once.
		if (TryRecoverDevice(device) && TryQueryEstimatedTime(device, out seconds))
		{
			if (seconds != PInvoke.BATTERY_UNKNOWN_TIME) estimatedTimeInSeconds = seconds;
			return true;
		}

		return false;
	}

	private static bool TryRecoverDevice(BatteryDevice device)
	{
		// Re-query the battery tag on the existing handle first.
		if (TryQueryTag(device.Handle, out var newTag))
		{
			device.Tag = newTag;
			TryUpdateBatteryInformation(device);
			return true;
		}

		// If the handle itself is stale, reopen the device and re-query the tag.
		var battery = PInvoke.CreateFile(device.DevicePath, (uint)FileAccess.ReadWrite, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
		if (battery.IsInvalid) return false;

		if (TryQueryTag(battery, out newTag))
		{
			device.Handle.Dispose();
			device.Handle = battery;
			device.Tag = newTag;
			TryUpdateBatteryInformation(device);
			return true;
		}

		battery.Dispose();
		return false;
	}

	private static void TryUpdateBatteryInformation(BatteryDevice device)
	{
		// The tag is invalidated when the battery characteristics change (e.g., a battery swap),
		// so the capacities captured at enumeration may be stale after a tag change.
		if (!TryQueryBatteryInformation(device.Handle, device.Tag, out var batteryInformation)) return;

		device.DesignedCapacity = batteryInformation.DesignedCapacity;
		device.FullChargedCapacity = batteryInformation.FullChargedCapacity;
	}

	private static unsafe bool TryQueryBatteryInformation(SafeFileHandle handle, uint batteryTag, out BATTERY_INFORMATION batteryInformation)
	{
		batteryInformation = default;

		var bqi = default(BATTERY_QUERY_INFORMATION);
		bqi.BatteryTag = batteryTag;
		bqi.InformationLevel = BATTERY_QUERY_INFORMATION_LEVEL.BatteryInformation;
		var bytesReturned = 0u;
		var information = default(BATTERY_INFORMATION);
		if (!PInvoke.DeviceIoControl((HANDLE)handle.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_INFORMATION, &bqi, (uint)sizeof(BATTERY_QUERY_INFORMATION), &information, (uint)sizeof(BATTERY_INFORMATION), &bytesReturned, null)) return false;

		batteryInformation = information;
		return true;
	}

	private static unsafe bool TryQueryTag(SafeFileHandle handle, out uint batteryTag)
	{
		batteryTag = 0;

		var dwWait = 0u;
		var bytesReturned = 0u;
		var tag = 0u;
		if (!PInvoke.DeviceIoControl((HANDLE)handle.DangerousGetHandle(), PInvoke.IOCTL_BATTERY_QUERY_TAG, &dwWait, sizeof(uint), &tag, sizeof(uint), &bytesReturned, null)) return false;

		batteryTag = tag;
		return true;
	}

	private sealed class BatteryDevice(string devicePath, SafeFileHandle handle, uint tag, uint designedCapacity, uint fullChargedCapacity)
	{
		public string DevicePath { get; } = devicePath;
		public SafeFileHandle Handle { get; set; } = handle;
		public uint Tag { get; set; } = tag;
		public uint DesignedCapacity { get; set; } = designedCapacity;
		public uint FullChargedCapacity { get; set; } = fullChargedCapacity;
	}
}
