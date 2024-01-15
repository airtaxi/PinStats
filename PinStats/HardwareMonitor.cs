using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using Windows.ApplicationModel;
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

	private static Computer Computer;
	private readonly static List<IHardware> CpuHardware = new();
	private readonly static List<IHardware> GpuHardware = new();
	private readonly static List<IHardware> NetworkHardware = new();
	private readonly static List<IHardware> StorageHardware = new();
	private readonly static List<IHardware> MemoryHardware = new();
	private readonly static List<IHardware> BatteryHardware = new();
	private readonly static SemaphoreSlim HardwareSemaphore = new(1, 1);

	// Motherboard hardware is not a list because only one motherboard is assumed to be present.
	private static IHardware s_motherboardHardware;

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

    public static bool ShouldUpdate { get; set; } = true;


    static HardwareMonitor()
	{
		// Initialize Hardware
		_ = RefreshComputerHardwareAsync();

		// Initialize Events
		SystemEvents.PowerModeChanged += OnPowerModeChanged;

		// Initialize Timers
		NetworkTimer.Elapsed += OnNetworkTimerElapsed;
		NetworkTimer.Start();
		OnNetworkTimerElapsed(NetworkTimer, null);

		StorageTimer.Elapsed += OnStorageTimerElapsed;
		StorageTimer.Start();
		OnStorageTimerElapsed(StorageTimer, null);

		CpuUsageTimer.Elapsed += OnCpuUsageTimerElapsed;
		CpuUsageTimer.Start();
		OnCpuUsageTimerElapsed(CpuUsageTimer, null);

		GpuUsageTimer.Elapsed += OnGpuUsageTimerElapsed;
		GpuUsageTimer.Start();
		OnGpuUsageTimerElapsed(GpuUsageTimer, null);

    }

	private static bool s_refreshingComputerHardware = false;
	public static async Task RefreshComputerHardwareAsync()
	{
		if(s_refreshingComputerHardware) return; // Prevent multiple refreshes.

		s_refreshingComputerHardware = true;

        if (Computer != null)
		{
			// Closing computer takes a lot of time.
			// Caching Computer and closing it in a separate thread improves performance.
			_ = Task.Run(() =>
			{
				var computer = Computer;
				computer.Close();
			});
        }
        // Configure new Computer
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
        Computer.HardwareRemoved += OnComputerHardwareRemoved;
        Computer.HardwareAdded += OnComputerHardwareAdded;

        // Refresh Computer Hardware in Task.Run to prevent blocking the UI thread.
        await Task.Run(() =>
		{
			// Close (Optional) and Open Computer to refresh Hardware.
			Computer.Open(); // This method takes a lot of time. It is recommended to call this method in a separate thread.
			Computer.Accept(new UpdateVisitor());

            // Refresh Hardware
            RefreshHardware();
		});
		// Setup Motherboard hardware
		s_motherboardHardware ??= Computer.Hardware.Where(x => x.HardwareType == HardwareType.Motherboard).FirstOrDefault();

		s_refreshingComputerHardware = false;
	}

	private static void RefreshHardware()
	{
		HardwareSemaphore.Wait();
		try
		{
			CpuHardware.Clear();
			GpuHardware.Clear();
			NetworkHardware.Clear();
			StorageHardware.Clear();
			MemoryHardware.Clear();
			BatteryHardware.Clear();

			foreach (var hardware in Computer.Hardware)
			{
				if (hardware.HardwareType == HardwareType.Cpu)
				{
					CpuHardware.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel)
				{
					GpuHardware.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Network)
				{
					NetworkHardware.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Storage)
				{
					StorageHardware.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Memory)
				{
					MemoryHardware.Add(hardware);
					continue;
				}
				else if (hardware.HardwareType == HardwareType.Battery)
				{
					BatteryHardware.Add(hardware);
					continue;
				}
			}
			GpuHardware.Reverse();
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static void OnComputerHardwareRemoved(IHardware hardware)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (hardware.HardwareType == HardwareType.Cpu) CpuHardware.Remove(hardware);
			else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel) GpuHardware.Remove(hardware);
			else if (hardware.HardwareType == HardwareType.Network) NetworkHardware.Remove(hardware);
			else if (hardware.HardwareType == HardwareType.Storage) StorageHardware.Remove(hardware);
			else if (hardware.HardwareType == HardwareType.Memory) MemoryHardware.Remove(hardware);
			else if (hardware.HardwareType == HardwareType.Battery) BatteryHardware.Remove(hardware);
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static void OnComputerHardwareAdded(IHardware hardware)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (hardware.HardwareType == HardwareType.Cpu) CpuHardware.Add(hardware);
			else if (hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel) GpuHardware.Add(hardware);
			else if (hardware.HardwareType == HardwareType.Network) NetworkHardware.Add(hardware);
			else if (hardware.HardwareType == HardwareType.Storage) StorageHardware.Add(hardware);
			else if (hardware.HardwareType == HardwareType.Memory) MemoryHardware.Add(hardware);
			else if (hardware.HardwareType == HardwareType.Battery) BatteryHardware.Add(hardware);
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static string GetMotherboardName() => s_motherboardHardware.Name;

	public static List<string> GetGpuHardwareNames()
	{
		HardwareSemaphore.Wait();
		try { return GpuHardware.Select(x => x.Name).ToList(); }
		finally { HardwareSemaphore.Release(); }
	}

	public static float GetAverageCpuUsage() => s_cpuUsage;

	public static float? GetAverageCpuTemperature(bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (update) CpuHardware.ForEach(x => x.Update());
			var cpuTemperatureSensors = CpuHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Temperature);

			var temperature = cpuTemperatureSensors.Where(x => x.Value != null && x.Value != float.NaN).Average(x => x.Value) ?? 0;
			return temperature > 0 ? temperature : null;
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static float s_lastTotalCpuPackagePower;
	public static float GetTotalCpuPackagePower(bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (update) CpuHardware.ForEach(x => x.Update());
			var cpuPowerSensors = CpuHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power);

			var cpuPackagePower = cpuPowerSensors.Where(x => x.Name.EndsWith("Package")).Sum(x => x.Value);
			if (cpuPackagePower.HasValue) s_lastTotalCpuPackagePower = cpuPackagePower.Value;
			return cpuPackagePower ?? s_lastTotalCpuPackagePower;
        }
		finally { HardwareSemaphore.Release(); }
	}

	public static float GetCurrentGpuUsage() => s_gpuUsage;

    private static float s_lastCurrentGpuPower;
    public static float GetCurrentGpuPower(bool update = false)
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return 0; // If there are no GPUs, return 0

		if (update) gpuHardware.Update();

		ISensor gpuPowerSensor = null;
		if (gpuHardware.HardwareType == HardwareType.GpuAmd) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Core");
		else if (gpuHardware.HardwareType == HardwareType.GpuNvidia) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Package");
		else if (gpuHardware.HardwareType == HardwareType.GpuIntel) gpuPowerSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name == "GPU Power");

		if (gpuPowerSensor?.Value.HasValue ?? false) s_lastCurrentGpuPower = gpuPowerSensor.Value.Value;

		return gpuPowerSensor?.Value ?? s_lastCurrentGpuPower;
	}

	public static float? GetCurrentGpuTemperature(bool update = false)
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return null; // If there are no GPUs, return null

		if (update) gpuHardware.Update();

		var gpuTemperatureSensors = gpuHardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value != float.NaN).ToList();
		if (gpuTemperatureSensors.Count == 0) return null;

		var temperature = gpuTemperatureSensors.Average(x => x.Value);
		return temperature ?? 0;
	}

	public static string GetCpuName()
	{
		if(CpuHardware.Count == 0) return "N/A"; // If there are no CPUs, return N/A.

		var firstCpuHardware = CpuHardware[0];
		if (CpuHardware.Count == 1) return firstCpuHardware.Name;
		else return firstCpuHardware.Name + " + " + (CpuHardware.Count - 1) + " more";
	}

	public static string GetCurrentGpuName()
	{
		var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return "N/A"; // If there are no GPUs, return N/A.

		return gpuHardware.Name;
	}

	// PC with no battery often has Battery Hardware with no sensors.
	// So we need to check if there are any sensors.
	// This method checks if there are any sensors by checking battery percentage.
	public static bool HasBattery() => GetTotalBatteryPercent() != null;

	public static float? GetTotalBatteryPercent(bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (BatteryHardware == null) return null;
			if (update) BatteryHardware.ForEach(x => x.Update());

			var fullChargedCapacity = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Full Charged Capacity").Sum(x => x.Value) ?? 0;
			if (fullChargedCapacity == 0) return null;

			var remainingCapacity = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Remaining Capacity").Sum(x => x.Value) ?? 0;

			return remainingCapacity / fullChargedCapacity * 100;
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static float? GetTotalBatteryChargeRate(bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (BatteryHardware == null) return null;
			if (update) BatteryHardware.ForEach(x => x.Update());

			var dischargeRateSensors = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power && x.Name.StartsWith("Discharge"));
			var chargeRateSensors = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Power && x.Name.StartsWith("Charge"));

			var chargeRate = chargeRateSensors.Sum(x => x.Value) ?? 0;
			var dischargeRate = dischargeRateSensors.Sum(x => x.Value * -1) ?? 0;

			var result = chargeRate + dischargeRate;
			return result;
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static float? GetAverageBatteryHealthPercent(bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (BatteryHardware == null) return null;
			if (update) BatteryHardware.ForEach(x => x.Update());

			var designedCapacitySensors = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Designed Capacity");
			var fullyChargedCapacitySensors = BatteryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Energy && x.Name == "Full Charged Capacity");

			var designedCapacitySum = designedCapacitySensors.Sum(x => x.Value) ?? 0;
			var fullyChargedCapacitySum = fullyChargedCapacitySensors.Sum(x => x.Value) ?? 0;
			if (designedCapacitySum == 0 || fullyChargedCapacitySum == 0) return null;

			var percent = fullyChargedCapacitySum / designedCapacitySum * 100;
			return percent;
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static IHardware GetCurrentGpuHardware()
	{
		var gpuIndex = Configuration.GetValue<int?>("GpuIndex") ?? 0;

		// If there are no GPUs, return null.
		if (GpuHardware.Count == 0) return null;

		if (gpuIndex > GpuHardware.Count) // User might have removed a GPU.
		{
			gpuIndex = 0;
			Configuration.SetValue("GpuIndex", gpuIndex);
		}

		return GpuHardware[gpuIndex];
	}

	public static string GetMemoryInformationText(bool queryVirtualMemory = false, bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (update) MemoryHardware.ForEach(x => x.Update());

			var memoryUsedSensors = MemoryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
			var memoryAvailableSensors = MemoryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Available");

			var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
			var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;

			var totalMemory = memoryUsed + memoryAvailable;
			return $"{memoryUsed:N2} GB (Total: {totalMemory:N2} GB) / {memoryAvailable:N2} GB Left";
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static float GetTotalMemory(bool queryVirtualMemory = false, bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (update) MemoryHardware.ForEach(x => x.Update());

			var memoryUsedSensors = MemoryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
			var memoryAvailableSensors = MemoryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Available");

			var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
			var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;

			var totalMemory = memoryUsed + memoryAvailable;
			return totalMemory;
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static float GetUsedMemory(bool queryVirtualMemory = false, bool update = false)
	{
		HardwareSemaphore.Wait();
		try
		{
			if (update) MemoryHardware.ForEach(x => x.Update());

			var memoryUsedSensors = MemoryHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == (queryVirtualMemory ? "Virtual " : string.Empty) + "Memory Used");
			return memoryUsedSensors.Sum(x => x.Value) ?? 0;
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static float GetStorageReadRateInBytes()
	{
		HardwareSemaphore.Wait();
		try
		{
			var sensors = StorageHardware.SelectMany(x => x.Sensors).Where(x => x.Name == "Read Rate");

			var total = sensors.Sum(x => x.Value) ?? 0;
			return total;
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static float GetStorageWriteRateInBytes()
	{
		HardwareSemaphore.Wait();
		try
		{
			var sensors = StorageHardware.SelectMany(x => x.Sensors).Where(x => x.Name == "Write Rate");

			var total = sensors.Sum(x => x.Value) ?? 0;
			return total;
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static float GetStorageReadRatePerSecondInBytes() => s_storageReadRatePerSecondInBytes;
	public static float GetStorageWriteRatePerSecondInBytes() => s_storageWriteRatePerSecondInBytes;

	public static long GetNetworkTotalUploadSpeedInBytes() => s_uploadSpeedInBytes;
	public static long GetNetworkTotalDownloadSpeedInBytes() => s_downloadSpeedInBytes;

	private static long GetNetworkTotalUploadedInBytes()
	{
		HardwareSemaphore.Wait();
		try
		{
			var sensors = NetworkHardware.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Uploaded");

			var total = sensors.Sum(x => x.Value) ?? 0;
			return (long)(total * (double)0x40000000);
		}
		finally { HardwareSemaphore.Release(); }
	}

	private static long GetNetworkTotalDownloadedInBytes()
	{
		HardwareSemaphore.Wait();
		try
		{
			var sensors = NetworkHardware.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Downloaded");

			var total = sensors.Sum(x => x.Value) ?? 0;
			return (long)(total * (double)0x40000000);
		}
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateCpuHardware()
	{
		HardwareSemaphore.Wait();
		try { CpuHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateGpuHardware()
	{
		HardwareSemaphore.Wait();
		try { GpuHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateNetworkHardware()
	{
		HardwareSemaphore.Wait();
		try { NetworkHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateStorageHardware()
	{
		HardwareSemaphore.Wait();
		try { StorageHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateMemoryHardware()
	{
		HardwareSemaphore.Wait();
		try { MemoryHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateBatteryHardware()
	{
		HardwareSemaphore.Wait();
		try { BatteryHardware.ForEach(x => x.Update()); }
		finally { HardwareSemaphore.Release(); }
	}

	public static void UpdateCurrentGpuHardware() => GetCurrentGpuHardware()?.Update();

	private static long s_bytesUploaded;
	private static long s_bytesDownloaded;

	private static void OnNetworkTimerElapsed(object sender, ElapsedEventArgs e)
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

	private static void OnStorageTimerElapsed(object sender, ElapsedEventArgs e)
    {
        // If the system is in sleep or hibernate mode, don't update the hardware information.
        if (!ShouldUpdate) return;

        UpdateStorageHardware();
		s_storageReadRatePerSecondInBytes = GetStorageReadRateInBytes();
		s_storageWriteRatePerSecondInBytes = GetStorageWriteRateInBytes();
	}

	private static void OnCpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
    {
        // If the system is in sleep or hibernate mode, don't update the hardware information.
        if (!ShouldUpdate) return;

        // Updating CPU Hardware takes a lot of time. Caching CPU Hardware and updating them in a separate thread improves performance.
        IHardware[] cpuHardware;
		HardwareSemaphore.Wait();
        try { cpuHardware = CpuHardware.ToArray(); } // Use ToArray instead of ToList to reduce memory usage.
        finally { HardwareSemaphore.Release(); }

		// Array doesn't support ForEach method. Use foreach instead.
		foreach (var cpu in cpuHardware) cpu.Update();
		var cpuTotalSensors = cpuHardware.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Load && x.Name == "CPU Total");

		var usage = cpuTotalSensors.Average(x => x.Value) ?? 0;

		LastCpuUsages.Add(usage);
		if (LastCpuUsages.Count > UsageCacheCount) LastCpuUsages.RemoveAt(0);

		s_cpuUsage = LastCpuUsages.Average();
	}

	private static void OnGpuUsageTimerElapsed(object sender, ElapsedEventArgs e)
    {
        // If the system is in sleep or hibernate mode, don't update the hardware information.
        if (!ShouldUpdate) return;

        var gpuHardware = GetCurrentGpuHardware();
		if (gpuHardware == null) return; // If there are no GPUs, return.

		gpuHardware.Update();

		var gpuLoadSensor = gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "GPU Core")
			?? gpuHardware.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "D3D 3D");
		var value = gpuLoadSensor?.Value ?? 0;
		value = Math.Min(value, 100); // Intel GPU load sensor returns 0-100, but it can exceed 100. Clamp it to 100.
		LastGpuUsages.Add(value);

		if (LastGpuUsages.Count > UsageCacheCount) LastGpuUsages.RemoveAt(0);
		s_gpuUsage = LastGpuUsages.Average();
    }

    private static async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
		// If the system is in sleep or hibernate mode, don't update the hardware information.
		if (e.Mode == PowerModes.Suspend) ShouldUpdate = false;
		else if (e.Mode == PowerModes.Resume) ShouldUpdate = true;

		// Power mode changes like AC power to battery or vice versa causes hardware monitor's battery information to be refreshed
		// (Seems like LibreHardwareMonitor's bug)
		else if (e.Mode == PowerModes.StatusChange) await RefreshComputerHardwareAsync();
    }
}
