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
	private const string MultimediaName = "MULTIMEDIA";
	private const string SystemName = "SYSTEM";
	private const string SystemOnChipName = "SOC";
	private const float MilliwattsPerWatt = 1000f;

	public static float GetTotalCpuPackagePower() => GetPowerMeterValues().TotalCpuPackagePower;

	public static float GetCurrentGpuPower() => GetPowerMeterValues().CurrentGpuPower;

	public static Arm64PowerMeterValues GetPowerMeterValues()
	{
		try
		{
			var powerValues = GetPowerValues();
			var cpuPowerValues = powerValues.Where(x => x.Name.StartsWith(CpuClusterNamePrefix, StringComparison.OrdinalIgnoreCase)).ToList();
			var totalCpuPowerInWatts = ConvertMilliwattsToWatts(cpuPowerValues.Sum(x => x.PowerInMilliwatts));
			var currentGpuPowerInWatts = GetPowerInWatts(powerValues, GpuName);
			var multimediaPowerInWatts = GetPowerInWatts(powerValues, MultimediaName);
			var systemPowerInWatts = GetPowerInWatts(powerValues, SystemName);
			var systemOnChipPowerInWatts = GetPowerInWatts(powerValues, SystemOnChipName);

			return new(totalCpuPowerInWatts, currentGpuPowerInWatts, multimediaPowerInWatts, systemOnChipPowerInWatts, systemPowerInWatts);
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return default;
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

	private static float GetPowerInWatts(List<PowerValue> powerValues, string name)
	{
		var powerInMilliwatts = powerValues.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).PowerInMilliwatts;
		return ConvertMilliwattsToWatts(powerInMilliwatts);
	}

	private static float ConvertMilliwattsToWatts(float powerInMilliwatts) => powerInMilliwatts / MilliwattsPerWatt;

	private readonly record struct PowerValue(string Name, float PowerInMilliwatts);
}

public readonly record struct Arm64PowerMeterValues(
	float TotalCpuPackagePower,
	float CurrentGpuPower,
	float MultimediaPower,
	float SystemOnChipPower,
	float SystemPower);
