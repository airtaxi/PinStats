using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using PinStats.Helpers;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Timers;
using Timer = System.Timers.Timer;

namespace PinStats;

public sealed class UpdateVisitor : IVisitor
{
	public void VisitComputer(IComputer computer)
	{
		computer.Traverse(this);
	}

	public void VisitHardware(IHardware hardware)
	{
		hardware.Update();
		foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this);
	}

	public void VisitSensor(ISensor sensor) => _ = sensor; // Do nothing. Sensor values are updated in VisitHardware method.
    public void VisitParameter(IParameter parameter) => _ = parameter; // Do nothing. Parameters are not used in this application.
}

public static class HardwareMonitor
{
	private const int UsageCacheCount = 20;
	private const int UsageTimerIntervalInMilliseconds = 50;

	// Should not be changed in the future because KiloBytesPer"Second" is calculated based on this value.
	private const int StorageTimerInMilliseconds = 1000;
	private const int NetworkTimerInMilliseconds = 1000;

	private static Computer s_computer;
	private static IHardware[] s_cpuHardwares = [];
	private static IHardware[] s_gpuHardwares = [];
	private static IHardware[] s_networkHardwares = [];
	private static IHardware[] s_storageHardwares = [];
	private static IHardware[] s_memoryHardwares = [];
	private static IHardware[] s_batteryHardwares = [];
	private static int s_refreshComputerHardwareInProgress;
	private static int s_cpuHardwareUpdateInProgress;
	private static int s_gpuHardwareUpdateInProgress;
	private static int s_networkHardwareUpdateInProgress;
	private static int s_storageHardwareUpdateInProgress;
	private static int s_memoryHardwareUpdateInProgress;
	private static int s_batteryHardwareUpdateInProgress;
	private static int s_networkTimerInProgress;
	private static int s_storageTimerInProgress;
	private static int s_cpuUsageTimerInProgress;
	private static int s_gpuUsageTimerInProgress;
	private static string s_fallbackCpuHardwareName;
	private static string s_fallbackGpuHardwareName;

	private static readonly Timer s_storageTimer = new() { Interval = StorageTimerInMilliseconds };
	private static float s_storageReadRatePerSecondInBytes;
	private static float s_storageWriteRatePerSecondInBytes;

	private static readonly Timer s_networkTimer = new() { Interval = NetworkTimerInMilliseconds };
	private static long s_downloadSpeedInBytes;
	private static long s_uploadSpeedInBytes;

	private static readonly Timer s_cpuUsageTimer = new() { Interval = UsageTimerIntervalInMilliseconds };
	private static readonly List<float> s_lastCpuUsages = [];
	private static float s_cpuUsage;

	private static readonly Timer s_gpuUsageTimer = new() { Interval = IsArm64Architecture ? UsageTimerIntervalInMilliseconds * 10 : UsageTimerIntervalInMilliseconds };
	private static readonly List<float> s_lastGpuUsages = [];
	private static float s_gpuUsage;

	public static bool ShouldUpdate { get; set; } = true;

	public static bool IsArm64Architecture => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

	private static bool IsX86OrX64Architecture => RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64;

	private static IHardware[] CpuHardwares => Volatile.Read(ref s_cpuHardwares);
	private static IHardware[] GpuHardwares => Volatile.Read(ref s_gpuHardwares);
	private static IHardware[] NetworkHardwares => Volatile.Read(ref s_networkHardwares);
	private static IHardware[] StorageHardwares => Volatile.Read(ref s_storageHardwares);
	private static IHardware[] MemoryHardwares => Volatile.Read(ref s_memoryHardwares);
	private static IHardware[] BatteryHardwares => Volatile.Read(ref s_batteryHardwares);

	static HardwareMonitor()
	{
		// Initialize Hardware
		Task.Run(RefreshComputerHardwareAsync);

		// SystemEvents.PowerModeChanged is used to detect Sleep and Hibernate modes.
		SystemEvents.PowerModeChanged += OnPowerModeChanged;
		// SystemEvents.SessionSwitch is used to detect Sleep mode with modern standby.
		SystemEvents.SessionSwitch += OnSessionSwitch;

		// Initialize Timers
		s_networkTimer.Elapsed += OnNetworkTimerElapsed;
		s_networkTimer.Start();
		OnNetworkTimerElapsed(s_networkTimer, null);

		s_storageTimer.Elapsed += OnStorageTimerElapsed;
		s_storageTimer.Start();
		OnStorageTimerElapsed(s_storageTimer, null);

		s_cpuUsageTimer.Elapsed += OnCpuUsageTimerElapsed;
		s_cpuUsageTimer.Start();
		OnCpuUsageTimerElapsed(s_cpuUsageTimer, null);

		s_gpuUsageTimer.Elapsed += OnGpuUsageTimerElapsed;
		s_gpuUsageTimer.Start();
		OnGpuUsageTimerElapsed(s_gpuUsageTimer, null);
	}

	public static async Task RefreshComputerHardwareAsync()
	{
		if (Interlocked.Exchange(ref s_refreshComputerHardwareInProgress, 1) == 1) return;

		try
		{
			var previousComputer = Volatile.Read(ref s_computer);

			// Configure new Computer
			var computer = new Computer
			{
				IsCpuEnabled = IsX86OrX64Architecture,
				IsGpuEnabled = IsX86OrX64Architecture,
				IsMemoryEnabled = true,
				IsNetworkEnabled = true,
				IsBatteryEnabled = true,
				IsStorageEnabled = true,
				IsMotherboardEnabled = true,
			};
			computer.HardwareRemoved += OnComputerHardwareAddedOrRemoved;
			computer.HardwareAdded += OnComputerHardwareAddedOrRemoved;
			Volatile.Write(ref s_computer, computer);

			if (!IsX86OrX64Architecture)
			{
				using (var searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
				{
					var gpuName = searcher.Get().Cast<ManagementObject>().FirstOrDefault(managementObject => managementObject["VideoProcessor"] != null)?["Name"]?.ToString();
					s_fallbackGpuHardwareName = gpuName;
				}

				using (var searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
				{
					var managementObjects = searcher.Get();

					var cpuName = default(string);
					var cpuCount = 0;
					foreach (var managementObject in managementObjects)
					{
						if (string.IsNullOrEmpty(cpuName)) cpuName = managementObject["Name"].ToString();
						cpuCount++;
						managementObject.Dispose();
					}

				if (cpuCount == 0) s_fallbackCpuHardwareName = App.Localization.GetLocalizedString("Info.NotAvailable");
				else if (cpuCount == 1) s_fallbackCpuHardwareName = cpuName;
				else s_fallbackCpuHardwareName = App.Localization.GetFormattedString("Hardware.MultiCpuFormat", cpuName, cpuCount - 1);
				}
			}

			// Refresh Computer Hardware in Task.Run to prevent blocking the UI thread.
			await Task.Run(() =>
			{
				// Close (Optional) and Open Computer to refresh Hardware.
				computer.Open(); // This method takes a lot of time. It is recommended to call this method in a separate thread.
				computer.Accept(new UpdateVisitor());

				// Refresh Hardware
				RefreshHardware(computer);
			});

			CloseComputerAsync(previousComputer);
		}
		catch (Exception exception) { App.WriteException(exception); }
		finally { Volatile.Write(ref s_refreshComputerHardwareInProgress, 0); }
	}

	private static void CloseComputerAsync(Computer computer)
	{
		if (computer == null) return;

		// Closing computer takes a lot of time.
		// Caching Computer and closing it in a separate thread improves performance.
		_ = Task.Run(() =>
		{
			try
			{
				computer.HardwareAdded -= OnComputerHardwareAddedOrRemoved;
				computer.HardwareRemoved -= OnComputerHardwareAddedOrRemoved;
				computer.Close();
			}
			catch (Exception exception) { App.WriteException(exception); }
		});
	}

	private static void RefreshHardware() => RefreshHardware(Volatile.Read(ref s_computer));

	private static void RefreshHardware(Computer computer)
	{
		try
		{
			if (computer == null) return;

			var cpuHardwares = new List<IHardware>();
			var gpuHardwares = new List<IHardware>();
			var networkHardwares = new List<IHardware>();
			var storageHardwares = new List<IHardware>();
			var memoryHardwares = new List<IHardware>();
			var batteryHardwares = new List<IHardware>();

			foreach (var hardware in computer.Hardware)
			{
				if (hardware.HardwareType == HardwareType.Cpu)
				{
					cpuHardwares.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel)
				{
					gpuHardwares.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Network)
				{
					networkHardwares.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Storage)
				{
					storageHardwares.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Memory)
				{
					memoryHardwares.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Battery)
				{
					batteryHardwares.Add(hardware);
					continue;
				}
			}
			gpuHardwares.Reverse();

			Volatile.Write(ref s_cpuHardwares, cpuHardwares.ToArray());
			Volatile.Write(ref s_gpuHardwares, gpuHardwares.ToArray());
			Volatile.Write(ref s_networkHardwares, networkHardwares.ToArray());
			Volatile.Write(ref s_storageHardwares, storageHardwares.ToArray());
			Volatile.Write(ref s_memoryHardwares, memoryHardwares.ToArray());
			Volatile.Write(ref s_batteryHardwares, batteryHardwares.ToArray());
		}
		catch (Exception exception) { App.WriteException(exception); }
	}

	private static void OnComputerHardwareAddedOrRemoved(IHardware hardware) => RefreshHardware();

	public static string GetMotherboardName()
	{
		try { return Volatile.Read(ref s_computer)?.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.Motherboard)?.Name ?? App.Localization.GetLocalizedString("Info.NotAvailable"); }
		catch (Exception exception)
		{
			App.WriteException(exception);
			return App.Localization.GetLocalizedString("Info.NotAvailable");
		}
	}

	private static bool TryUpdateHardware(IHardware[] hardwares, ref int updateInProgress)
	{
		if (hardwares.Length == 0) return true;
		if (Interlocked.Exchange(ref updateInProgress, 1) == 1) return false;

		try
		{
			foreach (var hardware in hardwares) hardware.Update();
			return true;
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return false;
		}
		finally { Volatile.Write(ref updateInProgress, 0); }
	}

	private static bool TryUpdateHardware(IHardware hardware, ref int updateInProgress)
	{
		if (Interlocked.Exchange(ref updateInProgress, 1) == 1) return false;

		try
		{
			hardware.Update();
			return true;
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return false;
		}
		finally { Volatile.Write(ref updateInProgress, 0); }
	}

	private static ISensor[] GetSensors(IHardware[] hardwares)
	{
		try { return [.. hardwares.SelectMany(x => x.Sensors)]; }
		catch (Exception exception)
		{
			App.WriteException(exception);
			return [];
		}
	}

	private static ISensor[] GetSensors(IHardware hardware)
	{
		try { return [.. hardware.Sensors]; }
		catch (Exception exception)
		{
			App.WriteException(exception);
			return [];
		}
	}

	public static List<string> GetGpuHardwareNames() => [.. GpuHardwares.Select(x => x.Name), s_fallbackGpuHardwareName];

	public static float GetAverageCpuUsage() => s_cpuUsage;

	public static float? GetAverageCpuTemperature(bool update = false)
	{
		var cpuHardwares = CpuHardwares;
		if (update) TryUpdateHardware(cpuHardwares, ref s_cpuHardwareUpdateInProgress);

		var cpuTemperatureSensors = GetSensors(cpuHardwares).Where(x => x.SensorType == SensorType.Temperature);

		var temperature = cpuTemperatureSensors.Where(x => x.Value != null && x.Value != float.NaN).Average(x => x.Value) ?? 0;
		return temperature > 0 ? temperature : null;
	}

	private static float s_lastTotalCpuPackagePower;
	public static float GetTotalCpuPackagePower(bool update = false)
	{
		if (IsArm64Architecture) return Arm64PowerMeterHelper.GetTotalCpuPackagePower();

		var cpuHardwares = CpuHardwares;
		if (update) TryUpdateHardware(cpuHardwares, ref s_cpuHardwareUpdateInProgress);

		var cpuPowerSensors = GetSensors(cpuHardwares).Where(x => x.SensorType == SensorType.Power);

		var cpuPackagePower = cpuPowerSensors.Where(x => x.Name.EndsWith("Package")).Sum(x => x.Value);
		if (cpuPackagePower.HasValue) s_lastTotalCpuPackagePower = cpuPackagePower.Value;
		return cpuPackagePower ?? s_lastTotalCpuPackagePower;
	}

	public static float GetCurrentGpuUsage() => s_gpuUsage;

	private static float s_lastCurrentGpuPower;
	public static float GetCurrentGpuPower(bool update = false)
	{
		if (IsArm64Architecture) return Arm64PowerMeterHelper.GetCurrentGpuPower();

		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return 0; // If there are no GPUs, return 0

		if (update) TryUpdateHardware(gpuHardware, ref s_gpuHardwareUpdateInProgress);

		var gpuPowerSensor = default(ISensor);
		var gpuSensors = GetSensors(gpuHardware);
		if (gpuHardware.HardwareType == HardwareType.GpuAmd) gpuPowerSensor = gpuSensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Core");
		else if (gpuHardware.HardwareType == HardwareType.GpuNvidia) gpuPowerSensor = gpuSensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Package");
		else if (gpuHardware.HardwareType == HardwareType.GpuIntel) gpuPowerSensor = gpuSensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Power");

		if (gpuPowerSensor?.Value.HasValue ?? false) s_lastCurrentGpuPower = gpuPowerSensor.Value.Value;

		return gpuPowerSensor?.Value ?? s_lastCurrentGpuPower;
	}

	public static Arm64PowerMeterValues GetArm64PowerMeterValues() => Arm64PowerMeterHelper.GetPowerMeterValues();

	public static float? GetCurrentGpuTemperature(bool update = false)
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return null; // If there are no GPUs, return null

		if (update) TryUpdateHardware(gpuHardware, ref s_gpuHardwareUpdateInProgress);

		var gpuTemperatureSensors = GetSensors(gpuHardware).Where(x => x.SensorType == SensorType.Temperature && x.Value != float.NaN).ToList();
		if (gpuTemperatureSensors.Count == 0) return null;

		var temperature = gpuTemperatureSensors.Average(x => x.Value);
		return temperature ?? 0;
	}

	public static string GetCpuName()
	{
		var cpuHardwares = CpuHardwares;
		if (cpuHardwares.Length == 0) return s_fallbackCpuHardwareName ?? App.Localization.GetLocalizedString("Info.NotAvailable");

		var firstCpuHardware = cpuHardwares[0];
		if (cpuHardwares.Length == 1) return firstCpuHardware.Name;
		else return App.Localization.GetFormattedString("Hardware.MultiCpuFormat", firstCpuHardware.Name, cpuHardwares.Length - 1);
	}

	public static string GetCurrentGpuName()
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null)
		{
			if (!string.IsNullOrEmpty(s_fallbackGpuHardwareName)) return s_fallbackGpuHardwareName;
			return App.Localization.GetLocalizedString("Info.NotAvailable"); // If there are no GPUs, return N/A.
		}

		return gpuHardware.Name;
	}

	// PC with no battery often has Battery Hardware with no sensors.
	// So we need to check if there are any sensors.
	// This method checks if there are any sensors by checking battery percentage.
	public static bool HasBattery() => GetTotalBatteryPercent() != null;

	public static float? GetTotalBatteryPercent(bool update = false)
	{
		var batteryHardwares = BatteryHardwares;
		if (batteryHardwares.Length == 0) return null;
		if (update) TryUpdateHardware(batteryHardwares, ref s_batteryHardwareUpdateInProgress);

		var batterySensors = GetSensors(batteryHardwares);
		var fullyChargedCapacity = batterySensors.Where(x => x.SensorType == SensorType.Energy && x.Name == "Fully-Charged Capacity").Sum(x => x.Value) ?? 0;
		if (fullyChargedCapacity == 0) return null;

		var remainingCapacity = batterySensors.Where(x => x.SensorType == SensorType.Energy && x.Name == "Remaining Capacity").Sum(x => x.Value) ?? 0;

		return remainingCapacity / fullyChargedCapacity * 100;
	}

	public static float? GetTotalBatteryChargeRate(bool update = false)
	{
		var batteryHardwares = BatteryHardwares;
		if (batteryHardwares.Length == 0) return null;
		if (update) TryUpdateHardware(batteryHardwares, ref s_batteryHardwareUpdateInProgress);

		var batterySensors = GetSensors(batteryHardwares);
		var dischargeRateSensors = batterySensors.Where(x => x.SensorType == SensorType.Power && x.Name == "Discharge Rate");
		var chargeRateSensors = batterySensors.Where(x => x.SensorType == SensorType.Power && x.Name == "Charge Rate");

		var chargeRate = chargeRateSensors.Sum(x => x.Value) ?? 0;
		var dischargeRate = dischargeRateSensors.Sum(x => x.Value * -1) ?? 0;

		var result = chargeRate + dischargeRate;
		return result;
	}

	public static TimeSpan? GetTotalBatteryEstimatedTime(bool update = false)
	{
		var batteryHardwares = BatteryHardwares;
		if (batteryHardwares.Length == 0) return null;
		if (update) TryUpdateHardware(batteryHardwares, ref s_batteryHardwareUpdateInProgress);

		var sensors = GetSensors(batteryHardwares).Where(x => x.SensorType == SensorType.TimeSpan);
		var remainingSeconds = sensors.Sum(x => x.Value) ?? 0;
		if (remainingSeconds == 0) return null;

		var result = TimeSpan.FromSeconds(remainingSeconds);
		return result;
	}

	public static float? GetAverageBatteryHealthPercent(bool update = false)
	{
		var batteryHardwares = BatteryHardwares;
		if (batteryHardwares.Length == 0) return null;
		if (update) TryUpdateHardware(batteryHardwares, ref s_batteryHardwareUpdateInProgress);

		var batterySensors = GetSensors(batteryHardwares);
		var designedCapacitySensors = batterySensors.Where(x => x.SensorType == SensorType.Energy && x.Name == "Designed Capacity");
		var fullyChargedCapacitySensors = batterySensors.Where(x => x.SensorType == SensorType.Energy && x.Name == "Fully-Charged Capacity");

		var designedCapacitySum = designedCapacitySensors.Sum(x => x.Value) ?? 0;
		var fullyChargedCapacitySum = fullyChargedCapacitySensors.Sum(x => x.Value) ?? 0;
		if (designedCapacitySum == 0 || fullyChargedCapacitySum == 0) return null;

		var percent = fullyChargedCapacitySum / designedCapacitySum * 100;
		return percent;
	}

	private static IHardware GetCurrentGpuHardware()
	{
		var gpuIndex = Configuration.GetValue<int?>("GpuIndex") ?? 0;
		var gpuHardwares = GpuHardwares;

		// If there are no GPUs, return null.
		if (gpuHardwares.Length == 0) return null;

		if (gpuIndex < 0 || gpuIndex >= gpuHardwares.Length) // User might have removed a GPU.
		{
			gpuIndex = 0;
			Configuration.SetValue("GpuIndex", gpuIndex);
		}

		return gpuHardwares[gpuIndex];
	}

	public static string GetMemoryInformationText(bool queryVirtualMemory = false, bool update = false)
	{
		var memoryHardwares = MemoryHardwares;
		if (update) TryUpdateHardware(memoryHardwares, ref s_memoryHardwareUpdateInProgress);

		var memoryHardwareName = queryVirtualMemory ? "Virtual Memory" : "Total Memory";
		var memorySensors = GetSensors(memoryHardwares);
		var memoryUsedSensors = memorySensors.Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Used" && x.Hardware.Name == memoryHardwareName);
		var memoryAvailableSensors = memorySensors.Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Available" && x.Hardware.Name == memoryHardwareName);

		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
		var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;

		var totalMemory = memoryUsed + memoryAvailable;
		return App.Localization.GetFormattedString("Info.MemoryInfoFormat", $"{memoryUsed:N2}", $"{totalMemory:N2}", $"{memoryAvailable:N2}");
	}

	public static float GetTotalMemory(bool queryVirtualMemory = false, bool update = false)
	{
		var memoryHardwares = MemoryHardwares;
		if (update) TryUpdateHardware(memoryHardwares, ref s_memoryHardwareUpdateInProgress);

		var memoryHardwareName = queryVirtualMemory ? "Virtual Memory" : "Total Memory";
		var memorySensors = GetSensors(memoryHardwares);
		var memoryUsedSensors = memorySensors.Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Used" && x.Hardware.Name == memoryHardwareName);
		var memoryAvailableSensors = memorySensors.Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Available" && x.Hardware.Name == memoryHardwareName);

		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
		var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;

		var totalMemory = memoryUsed + memoryAvailable;
		return totalMemory;
	}

	public static float GetUsedMemory(bool queryVirtualMemory = false, bool update = false)
	{
		var memoryHardwares = MemoryHardwares;
		if (update) TryUpdateHardware(memoryHardwares, ref s_memoryHardwareUpdateInProgress);

		var memoryHardwareName = queryVirtualMemory ? "Virtual Memory" : "Total Memory";
		var memoryUsedSensors = GetSensors(memoryHardwares).Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Used" && x.Hardware.Name == memoryHardwareName);
		return memoryUsedSensors.Sum(x => x.Value) ?? 0;
	}

	private static float GetStorageReadRateInBytes()
	{
		var sensors = GetSensors(StorageHardwares).Where(x => x.Name == "Read Rate");

		var total = sensors.Sum(x => x.Value) ?? 0;
		return total;
	}

	private static float GetStorageWriteRateInBytes()
	{
		var sensors = GetSensors(StorageHardwares).Where(x => x.Name == "Write Rate");

		var total = sensors.Sum(x => x.Value) ?? 0;
		return total;
	}

	public static float GetStorageReadRatePerSecondInBytes() => s_storageReadRatePerSecondInBytes;
	public static float GetStorageWriteRatePerSecondInBytes() => s_storageWriteRatePerSecondInBytes;

	public static long GetNetworkTotalUploadSpeedInBytes() => s_uploadSpeedInBytes;
	public static long GetNetworkTotalDownloadSpeedInBytes() => s_downloadSpeedInBytes;

	private static long GetNetworkTotalUploadedInBytes()
	{
		var sensors = GetSensors(NetworkHardwares).Where(x => x.Name == "Data Uploaded");

		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	private static long GetNetworkTotalDownloadedInBytes()
	{
		var sensors = GetSensors(NetworkHardwares).Where(x => x.Name == "Data Downloaded");

		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	public static void UpdateCpuHardware() => TryUpdateHardware(CpuHardwares, ref s_cpuHardwareUpdateInProgress);

	public static void UpdateGpuHardware() => TryUpdateHardware(GpuHardwares, ref s_gpuHardwareUpdateInProgress);

	public static void UpdateNetworkHardware() => TryUpdateHardware(NetworkHardwares, ref s_networkHardwareUpdateInProgress);

	public static void UpdateStorageHardware() => TryUpdateHardware(StorageHardwares, ref s_storageHardwareUpdateInProgress);

	public static void UpdateMemoryHardware() => TryUpdateHardware(MemoryHardwares, ref s_memoryHardwareUpdateInProgress);

	public static void UpdateBatteryHardware() => TryUpdateHardware(BatteryHardwares, ref s_batteryHardwareUpdateInProgress);

	public static void UpdateCurrentGpuHardware()
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware != null) TryUpdateHardware(gpuHardware, ref s_gpuHardwareUpdateInProgress);
	}

	private static long s_bytesUploaded;
	private static long s_bytesDownloaded;

	private static void OnNetworkTimerElapsed(object sender, ElapsedEventArgs e)
	{
		if (Interlocked.Exchange(ref s_networkTimerInProgress, 1) == 1) return;
		try
		{
			// If the system is in sleep or hibernate mode, don't update the hardware information.
			if (!ShouldUpdate) return;

			UpdateNetworkHardware();
			var totalUploadedBytes = GetNetworkTotalUploadedInBytes();
			var totalDownloadedBytes = GetNetworkTotalDownloadedInBytes();

			// Values can reset back at zero (eg: after waking from sleep).
			if (totalUploadedBytes < s_bytesUploaded || totalDownloadedBytes < s_bytesDownloaded)
			{
				s_bytesUploaded = 0;
				s_bytesDownloaded = 0;
			}

			s_uploadSpeedInBytes = totalUploadedBytes - s_bytesUploaded;
			s_downloadSpeedInBytes = totalDownloadedBytes - s_bytesDownloaded;

			s_bytesUploaded = totalUploadedBytes;
			s_bytesDownloaded = totalDownloadedBytes;
		}
		catch (Exception exception) { App.WriteException(exception); }
		finally { Volatile.Write(ref s_networkTimerInProgress, 0); }
	}

	private static void OnStorageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		if (Interlocked.Exchange(ref s_storageTimerInProgress, 1) == 1) return;
		try
		{
			// If the system is in sleep or hibernate mode, don't update the hardware information.
			if (!ShouldUpdate) return;

			UpdateStorageHardware();
			s_storageReadRatePerSecondInBytes = GetStorageReadRateInBytes();
			s_storageWriteRatePerSecondInBytes = GetStorageWriteRateInBytes();
		}
		catch (Exception exception) { App.WriteException(exception); }
		finally { Volatile.Write(ref s_storageTimerInProgress, 0); }
	}

	private static readonly PerformanceCounter s_cpuPerformanceCounter = new("Processor Information", "% Processor Utility", "_Total");
	private static void OnCpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		if (Interlocked.Exchange(ref s_cpuUsageTimerInProgress, 1) == 1) return;
		try
		{
			// If the system is in sleep or hibernate mode, don't update the hardware information.
			if (!ShouldUpdate) return;

			var usage = s_cpuPerformanceCounter.NextValue();
			usage = Math.Clamp(usage, 0, 100);

			s_lastCpuUsages.Add(usage);
			if (s_lastCpuUsages.Count > UsageCacheCount) s_lastCpuUsages.RemoveAt(0);

			s_cpuUsage = s_lastCpuUsages.Average();
		}
		catch (Exception exception) { App.WriteException(exception); }
		finally { Volatile.Write(ref s_cpuUsageTimerInProgress, 0); }
	}

	private static void OnGpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		if (Interlocked.Exchange(ref s_gpuUsageTimerInProgress, 1) == 1) return;
		try
		{
			// If the system is in sleep or hibernate mode, don't update the hardware information.
			if (!ShouldUpdate) return;

			var usage = 0f;

			if (IsX86OrX64Architecture)
			{
				var gpuHardware = GetCurrentGpuHardware();
				if (gpuHardware == null) return; // If there are no GPUs, return.

				TryUpdateHardware(gpuHardware, ref s_gpuHardwareUpdateInProgress);

				var gpuSensors = GetSensors(gpuHardware);
				var gpuLoadSensor = gpuSensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "GPU Core")
					?? gpuSensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "D3D 3D");
				usage = gpuLoadSensor?.Value ?? 0;
			}
			else usage = GpuPerformanceHelper.GetTotalUtilization();

			usage = Math.Clamp(usage, 0, 100); // Intel GPU load sensor returns 0-100, but it can exceed 100. Clamp it to 100.
			s_lastGpuUsages.Add(usage);

			if (s_lastGpuUsages.Count > UsageCacheCount) s_lastGpuUsages.RemoveAt(0);
			s_gpuUsage = s_lastGpuUsages.Average();
		}
		catch (Exception exception) { App.WriteException(exception); }
		finally { Volatile.Write(ref s_gpuUsageTimerInProgress, 0); }
	}

	private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
	{
		// If the system is in sleep or hibernate mode, don't update the hardware information.
		if (e.Mode == PowerModes.Suspend) ShouldUpdate = false;
		else if (e.Mode == PowerModes.Resume) ShouldUpdate = true;
		else if (e.Mode == PowerModes.StatusChange)
		{
			Task.Run(RefreshComputerHardwareAsync);
		}
	}

	private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
	{
		// If the system is in modern standby sleep mode, don't update the hardware information.
		if (e.Reason == SessionSwitchReason.SessionLock) ShouldUpdate = false;
		else if (e.Reason == SessionSwitchReason.SessionUnlock) ShouldUpdate = true;
	}
}
