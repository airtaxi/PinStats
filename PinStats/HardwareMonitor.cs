using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts.DataProvider;
using Windows.Networking.NetworkOperators;
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
	private readonly static Computer Computer;
	private readonly static List<IHardware> CpuHardwares = new();
	private readonly static List<IHardware> GpuHardwares = new();
	private readonly static List<IHardware> NetworkHardwares = new();
	private readonly static List<IHardware> StorageHardwares = new();
	private readonly static List<IHardware> MemoryHardwares = new();

	private readonly static Timer NetworkTimer = new() { Interval = 1000 };
	private static long s_downloadSpeedInBytes;
	private static long s_uploadSpeedInBytes;

	static HardwareMonitor()
	{
		Computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsNetworkEnabled = true,
			IsStorageEnabled = true
		};
		Computer.Open();
		Computer.Accept(new UpdateVisitor());

		foreach (var hardware in Computer.Hardware)
		{
			if (hardware.HardwareType == HardwareType.Cpu)
			{
				CpuHardwares.Add(hardware);
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
		}
		GpuHardwares.Reverse();

		NetworkTimer.Elapsed += OnNetworkTimerElapsed;

		NetworkTimer.Start();
	}

	public static List<string> GetGpuHardwareNames() => GpuHardwares.Select(x => x.Name).ToList();

	public static float GetAverageCpuUsage()
	{
		CpuHardwares.ForEach(x => x.Update());
		var cpuTotalSensors = CpuHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Load && x.Name == "CPU Total");
		return cpuTotalSensors.Average(x => x.Value) ?? 0;
	}
	
	public static float? GetAverageCpuTemperature()
	{
		CpuHardwares.ForEach(x => x.Update());
		var cpuTemperatureSensors = CpuHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Temperature);
		var temperature = cpuTemperatureSensors.Where(x => x.Value != null && x.Value != float.NaN).Average(x => x.Value) ?? 0;
		return temperature > 0 ? temperature : null;
	}

	public static float GetCurrentGpuUsage()
	{
		var gpuHardware = GetCurrentGpuHardware();
		gpuHardware.Update();

		var gpuLoadSensors = gpuHardware.Sensors.Where(x => x.SensorType == SensorType.Load && x.Value != float.NaN);
		var usage = gpuLoadSensors.Sum(x => x.Value);
		return usage ?? 0;
	}

	public static float? GetCurrentGpuTemperature()
	{
		var gpuHardware = GetCurrentGpuHardware();
		gpuHardware.Update();

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

	private static IHardware GetCurrentGpuHardware()
	{
		var gpuIndex = Configuration.GetValue<int?>("GpuIndex") ?? 0;
		if (gpuIndex > GpuHardwares.Count)
		{
			gpuIndex = 0;
			Configuration.SetValue("GpuIndex", gpuIndex);
		}
		var gpuHardware = GpuHardwares[gpuIndex];
		return gpuHardware;
	}

	public static string GetMemoryInformationText()
	{
		MemoryHardwares.ForEach(x => x.Update());
		var memoryUsedSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Used");
		var memoryUsed = memoryUsedSensors.Sum(x => x.Value) ?? 0;
		var memoryAvailableSensors = MemoryHardwares.SelectMany(x => x.Sensors).Where(x => x.SensorType == SensorType.Data && x.Name == "Memory Available");
		var memoryAvailable = memoryAvailableSensors.Sum(x => x.Value) ?? 0;
		return $"{memoryUsed:N2} GB ({memoryAvailable:N2} GB available)";
	}

	public static long GetNetworkTotalUploadSpeedInBytes() => s_uploadSpeedInBytes;
	public static long GetNetworkTotalDownloadSpeedInBytes() => s_downloadSpeedInBytes;

	private static long GetNetworkTotalUploadedInBytes()
	{
		NetworkHardwares.ForEach(x => x.Update());
		var sensors = NetworkHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Uploaded");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	private static long GetNetworkTotalDownloadedInBytes()
	{
		NetworkHardwares.ForEach(x => x.Update());
		var sensors = NetworkHardwares.SelectMany(x => x.Sensors).Where(x => x.Name == "Data Downloaded");
		var total = sensors.Sum(x => x.Value) ?? 0;
		return (long)(total * (double)0x40000000);
	}

	private static long s_bytesUploaded;
	private static long s_bytesDownloaded;
	private static void OnNetworkTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
	{
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
}
