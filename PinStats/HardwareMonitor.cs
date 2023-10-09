using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts.DataProvider;
using Windows.Networking.NetworkOperators;

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
	private readonly static IHardware CpuHardware;
	private readonly static List<IHardware> GpuHardwares = new();
	private readonly static List<IHardware> NetworkHardwares = new();
	private readonly static List<IHardware> StorageHardwares = new();
	private readonly static List<IHardware> MemoryHardwares = new();
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
				CpuHardware = hardware;
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
	}

}
