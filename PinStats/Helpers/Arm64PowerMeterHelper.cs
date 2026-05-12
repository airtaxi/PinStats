using System.Globalization;
using System.Management;

namespace PinStats.Helpers;

/// <summary>
/// Reads ARM64 SoC power telemetry from Windows WMI performance counter classes.
/// </summary>
public static class Arm64PowerMeterHelper
{
	private const string EnergyMeterQuery = "SELECT Name, Power FROM Win32_PerfFormattedData_PowerMeterCounter_EnergyMeter";
	private const string CpuClusterNamePrefix = "CPU_CLUSTER_";
	private const string GpuName = "GPU";
	private const float MilliwattsPerWatt = 1000f;

	public static float GetTotalCpuPackagePower()
	{
		try
		{
			var cpuPowerValues = GetPowerValues().Where(x => x.Name.StartsWith(CpuClusterNamePrefix, StringComparison.OrdinalIgnoreCase)).ToList();
			if (cpuPowerValues.Count == 0) return 0;

			var totalCpuPowerInMilliwatts = cpuPowerValues.Sum(x => x.PowerInMilliwatts);
			return totalCpuPowerInMilliwatts / MilliwattsPerWatt;
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return 0;
		}
	}

	public static float GetCurrentGpuPower()
	{
		try
		{
			var gpuPowerValues = GetPowerValues().Where(x => string.Equals(x.Name, GpuName, StringComparison.OrdinalIgnoreCase)).ToList();
			if (gpuPowerValues.Count == 0) return 0;

			var gpuPowerInMilliwatts = gpuPowerValues[0].PowerInMilliwatts;
			return gpuPowerInMilliwatts / MilliwattsPerWatt;
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return 0;
		}
	}

	private static List<PowerValue> GetPowerValues()
	{
		var powerValues = new List<PowerValue>();

		using var searcher = new ManagementObjectSearcher(EnergyMeterQuery);
		using var managementObjects = searcher.Get();

		foreach (var item in managementObjects)
		{
			var managementObject = (ManagementObject)item;
			try
			{
				var name = managementObject["Name"]?.ToString();
				if (string.IsNullOrWhiteSpace(name)) continue;

				var powerInMilliwatts = ConvertToSingle(managementObject["Power"]);
				powerValues.Add(new(name, powerInMilliwatts));
			}
			finally { managementObject.Dispose(); }
		}

		return powerValues;
	}

	private static float ConvertToSingle(object value)
	{
		if (value == null || value == DBNull.Value) return 0;
		return Convert.ToSingle(value, CultureInfo.InvariantCulture);
	}

	private readonly record struct PowerValue(string Name, float PowerInMilliwatts);
}
