using LibreHardwareMonitor.Hardware;
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
		foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
	}
	public void VisitSensor(ISensor sensor) { }
	public void VisitParameter(IParameter parameter) { }
}

public static class HardwareMonitor
{
	private const int UsageCacheCount = 20;
	private const int UsageTimerIntervalInMilliseconds = 50;

	// Should not be changed in the future because KiloBytesPer"Second" is calculated based on this value.
	private const int StorageTimerInMilliseconds = 1000;
	private const int NetworkTimerInMilliseconds = 1000;

	private readonly static Computer Computer;
	private readonly static List<IHardware> CpuHardwares = new();
	private readonly static List<IHardware> GpuHardwares = new();
	private readonly static List<IHardware> NetworkHardwares = new();
	private readonly static List<IHardware> StorageHardwares = new();
	private readonly static List<IHardware> MemoryHardwares = new();
	private readonly static List<IHardware> BatteryHardwares = new();

	// Motherboard hardware is not a list because only one motherboard is assumed to be present.
	private readonly static IHardware MotherboardHardware;

	private readonly static Timer StorageTimer = new() { Interval = StorageTimerInMilliseconds };
	private static float s_storageReadRatePerSecondInBytes;
	private static float s_storageWriteRatePerSecondInBytes;

	private readonly static Timer NetworkTimer = new() { Interval = NetworkTimerInMilliseconds };
	private static long s_downloadSpeedInBytes;
	private static long s_uploadSpeedInBytes;

	private readonly static Timer CpuUsageTimer = new() { Interval = UsageTimerIntervalInMilliseconds };
	private readonly static List<float> LastCpuUsages = new();
	private static float s_cpuUsage;

	private readonly static Timer GpuUsageTimer = new() { Interval = UsageTimerIntervalInMilliseconds };
	private readonly static List<float> LastGpuUsages = new();
	private static float s_gpuUsage;


	static HardwareMonitor()
	{
		Computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsNetworkEnabled = true,
			IsBatteryEnabled = true,
			IsStorageEnabled = true,
			IsMotherboardEnabled = true,
		};
		Computer.Open();
		Computer.Accept(new UpdateVisitor());
		Computer.HardwareRemoved += OnComputerHardwareRemoved;
		Computer.HardwareAdded += OnComputerHardwareAdded;

		foreach (var hardware in Computer.Hardware)
		{
			if (hardware.HardwareType == HardwareType.Cpu)
			{
				CpuHardwares.Add(hardware);
				continue;
			}
			else if (hardware.HardwareType == HardwareType.Motherboard)
			{
				MotherboardHardware = hardware;
				continue;
			}
			else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel)
			{
				GpuHardwares.Add(hardware);
				continue;
			}
			else if (hardware.HardwareType == HardwareType.Network)
			{
				NetworkHardwares.Add(hardware);
				continue;
			}
			else if (hardware.HardwareType == HardwareType.Storage)
			{
				StorageHardwares.Add(hardware);
				continue;
			}
			else if (hardware.HardwareType == HardwareType.Memory)
			{
				MemoryHardwares.Add(hardware);
				continue;
			}
			else if (hardware.HardwareType == HardwareType.Battery)
			{
				BatteryHardwares.Add(hardware);
				continue;
			}
		}
		GpuHardwares.Reverse();

		NetworkTimer.Elapsed += OnNetworkTimerElapsed;
		NetworkTimer.Start();

		StorageTimer.Elapsed += OnStorageTimerElapsed;
		StorageTimer.Start();

		CpuUsageTimer.Elapsed += OnCpuUsageTimerElapsed;
		CpuUsageTimer.Start();

		GpuUsageTimer.Elapsed += OnGpuUsageTimerElapsed;
		GpuUsageTimer.Start();
		OnGpuUsageTimerElapsed(GpuUsageTimer, null);
	}

	private static void OnComputerHardwareRemoved(IHardware hardware)
	{
		if (hardware.HardwareType == HardwareType.Cpu) CpuHardwares.Remove(hardware);
		else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel) GpuHardwares.Remove(hardware);
		else if (hardware.HardwareType == HardwareType.Network) NetworkHardwares.Remove(hardware);
		else if (hardware.HardwareType == HardwareType.Storage) StorageHardwares.Remove(hardware);
		else if (hardware.HardwareType == HardwareType.Memory) MemoryHardwares.Remove(hardware);
		else if (hardware.HardwareType == HardwareType.Battery) BatteryHardwares.Remove(hardware);
	}

	private static void OnComputerHardwareAdded(IHardware hardware)
	{
		if (hardware.HardwareType == HardwareType.Cpu) CpuHardwares.Add(hardware);
		else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel) GpuHardwares.Add(hardware);
		else if (hardware.HardwareType == HardwareType.Network) NetworkHardwares.Add(hardware);
		else if (hardware.HardwareType == HardwareType.Storage) StorageHardwares.Add(hardware);
		else if (hardware.HardwareType == HardwareType.Memory) MemoryHardwares.Add(hardware);
		else if (hardware.HardwareType == HardwareType.Battery) BatteryHardwares.Add(hardware);
	}

	public static string GetMotherboardName() => MotherboardHardware.Name;

	public static List<string> GetGpuHardwareNames() => GpuHardwares.Select(x => x.Name).ToList();

	public static float GetAverageCpuUsage() => s_cpuUsage;

	public static float? GetAverageCpuTemperature(bool update = false)
	{
		if (update) CpuHardwares.ForEach(x => x.Update());
		var cpuTemperatureSensors = CpuHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Temperature);
		var temperature = cpuTemperatureSensors.Where(x => x.Value != null && x.Value != float.NaN).Average(x => x.Value) ?? 0;
		return temperature > 0 ? temperature : null;
	}

	public static float GetTotalCpuPackagePower(bool update = false)
	{
		if (update) CpuHardwares.ForEach(x => x.Update());
		var cpuPowerSensors = CpuHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power);
		var cpuPackagePower = cpuPowerSensors.Where(x => x.Name.EndsWith("Package")).Sum(x => x.Value) ?? 0;
		return cpuPackagePower;
	}

	public static float GetCurrentGpuUsage() => s_gpuUsage;

	public static float GetCurrentGpuPower(bool update = false)
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (update) gpuHardware.Update();

		ISensor gpuPowerSensor = null;
		if (gpuHardware.HardwareType == HardwareType.GpuAmd) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Package");
		else if (gpuHardware.HardwareType == HardwareType.GpuNvidia) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Package");
		else if (gpuHardware.HardwareType == HardwareType.GpuIntel) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Power");

		return gpuPowerSensor?.Value ?? 0;
	}

	public static float? GetCurrentGpuTemperature(bool update = false)
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (update) gpuHardware.Update();

		var gpuTemperatureSensors = gpuHardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value != float.NaN).ToList();
		if (gpuTemperatureSensors.Count == 0) return null;

		var temperature = gpuTemperatureSensors.Average(x => x.Value);
		return temperature ?? 0;
	}

	public static string GetCpuName()
	{
		var firstCpuHardwre = CpuHardwares[0];
		if (CpuHardwares.Count == 1) return firstCpuHardwre.Name;
		else return firstCpuHardwre.Name + " + " + (CpuHardwares.Count - 1) + " more";
	}

	public static string GetCurrentGpuName()
	{
		var gpuHardware = GetCurrentGpuHardware();
		return gpuHardware.Name;
	}

	// PC with no battery often has Battery Hardware with no sensors.
	// So we need to check if there are any sensors.
	// This method checks if there are any sensors by checking battery percentage.
	public static bool HasBattery() => GetTotalBatteryPercent() != null;

	public static float? GetTotalBatteryPercent(bool update = false)
	{
		if(BatteryHardwares == null) return null;
		if (update) BatteryHardwares.ForEach(x => x.Update());

		var fullChargedCapacity = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Full Charged Capacity").Sum(x => x.Value) ?? 0;
		if (fullChargedCapacity == 0) return null;

		var remainingCapacity = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Remaining Capacity").Sum(x => x.Value) ?? 0;

		var percent = remainingCapacity / fullChargedCapacity * 100;
		return percent;
	}

	public static float? GetTotalBatteryChargeRate(bool update = false)
	{
		if(BatteryHardwares == null) return null;
		if (update) BatteryHardwares.ForEach(x => x.Update());

		var dischargeRateSensors = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power && x.Name.StartsWith("Discharge"));
		var chargeRateSensors = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power && x.Name.StartsWith("Charge"));
		var chargeRate = chargeRateSensors.Sum(x => x.Value) ?? 0;
		var dischargeRate = dischargeRateSensors.Sum(x => x.Value * -1) ?? 0;
		var result = chargeRate + dischargeRate;
		return result;
	}

	public static float? GetAverageBatteryHealthPercent(bool update = false)
	{
		if(BatteryHardwares == null) return null;
		if (update) BatteryHardwares.ForEach(x => x.Update());

		var designedCapacitySensors = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Designed Capacity");
		var fullyChargedCapacitySensors = BatteryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Full Charged Capacity");
		var designedCapacitySum = designedCapacitySensors.Sum(x => x.Value) ?? 0;
		var fullyChargedCapacitySum = fullyChargedCapacitySensors.Sum(x => x.Value) ?? 0;
		if (designedCapacitySum == 0 || fullyChargedCapacitySum == 0) return null;

		var percent = fullyChargedCapacitySum / designedCapacitySum * 100;
		return percent;
	}

	private static IHardware GetCurrentGpuHardware()
	{
		var gpuIndex = Configuration.GetValue<int?>("GpuIndex") ?? 0;
		if (gpuIndex > GpuHardwares.Count) // User might have removed a GPU.
		{
			gpuIndex = 0;
			Configuration.SetValue("GpuIndex", gpuIndex);
		}
		var gpuHardware = GpuHardwares[gpuIndex];
		return gpuHardware;
	}

	public static string GetMemoryInformationText(bool queryVirtualMemory = false)
	{
		MemoryHardwares.ForEach(x => x.Update());
		var memoryUsedSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;

		var memoryAvailableSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Available");
		var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;

		var totalMemory = memoryUsed + memoryAvailable;
		return $"{memoryUsed:N2} GB (Total: {totalMemory:N2} GB) / {memoryAvailable:N2} GB Left";
	}

	public static float GetTotalMemory(bool queryVirtualMemory = false, bool update = false)
	{
		if (update) MemoryHardwares.ForEach(x => x.Update());

		var memoryUsedSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;

		var memoryAvailableSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Available");
		var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;
		
		var totalMemory = memoryUsed + memoryAvailable;
		return totalMemory;
	}

	public static float GetUsedMemory(bool queryVirtualMemory = false, bool update = false)
	{
		if (update) MemoryHardwares.ForEach(x => x.Update());

		var memoryUsedSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
		return memoryUsed;
	}

	private static float GetStorageReadRateInBytes()
	{
		var sensors = StorageHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Read Rate");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return total;
	}

	private static float GetStorageWriteRateInBytes()
	{
		var sensors = StorageHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Write Rate");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return total;
	}

	public static float GetStorageReadRatePerSecondInBytes() => s_storageReadRatePerSecondInBytes;
	public static float GetStorageWriteRatePerSecondInBytes() => s_storageWriteRatePerSecondInBytes;

	public static long GetNetworkTotalUploadSpeedInBytes() => s_uploadSpeedInBytes;
	public static long GetNetworkTotalDownloadSpeedInBytes() => s_downloadSpeedInBytes;

	private static long GetNetworkTotalUploadedInBytes()
	{
		var sensors = NetworkHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Uploaded");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	private static long GetNetworkTotalDownloadedInBytes()
	{
		var sensors = NetworkHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Downloaded");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	public static void UpdateCpuHardwares() => CpuHardwares.ForEach(x => x.Update());
	public static void UpdateGpuHardwares() => GpuHardwares.ForEach(x => x.Update());
	public static void UpdateNetworkHardwares() => NetworkHardwares.ForEach(x => x.Update());
	public static void UpdateStorageHardwares() => StorageHardwares.ForEach(x => x.Update());
	public static void UpdateMemoryHardwares() => MemoryHardwares.ForEach(x => x.Update());
	public static void UpdateBatteryHardwares() => BatteryHardwares.ForEach(x => x.Update());
	public static void UpdateCurrentGpuHardware() => GetCurrentGpuHardware().Update();

	private static long s_bytesUploaded;
	private static long s_bytesDownloaded;
	private static void OnNetworkTimerElapsed(object sender, ElapsedEventArgs e)
	{
		UpdateNetworkHardwares();
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
	
	private static void OnStorageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		UpdateStorageHardwares();
		s_storageReadRatePerSecondInBytes = GetStorageReadRateInBytes();
		s_storageWriteRatePerSecondInBytes = GetStorageWriteRateInBytes();
	}

	private static void OnCpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		CpuHardwares.ForEach(x => x.Update());
		var cpuTotalSensors = CpuHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Load && x.Name == "CPU Total");
		var usage = cpuTotalSensors.Average(x => x.Value) ?? 0;
		LastCpuUsages.Add(usage);
		if (LastCpuUsages.Count > UsageCacheCount) LastCpuUsages.RemoveAt(0);
		s_cpuUsage = LastCpuUsages.Average();
	}

	private static void OnGpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
	{
		var gpuHardware = GetCurrentGpuHardware();
		gpuHardware.Update();

		if (gpuHardware.HardwareType == HardwareType.GpuIntel)
		{
			var gpuLoadSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "D3D 3D");
			var value = gpuLoadSensor?.Value ?? 0;
			value = Math.Min(value, 100); // Intel GPU load sensor returns 0-100, but it can exceed 100. Clamp it to 100.
			LastGpuUsages.Add(value);
		}
		else
		{
			var gpuLoadSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "GPU Core");
			var value = gpuLoadSensor?.Value ?? 0;
			LastGpuUsages.Add(value);
		}

		if (LastGpuUsages.Count > UsageCacheCount) LastGpuUsages.RemoveAt(0);
		s_gpuUsage = LastGpuUsages.Average();
	}
}
