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
	private const string SystemFallbackName = "SYS";
	private const string SystemOnChipName = "SOC";
	private const float MilliwattsPerWatt = 1000f;

	public static float GetTotalCpuPackagePower() => GetPowerMeterValues().TotalCpuPackagePower ?? 0;

	public static float GetCurrentGpuPower() => GetPowerMeterValues().CurrentGpuPower ?? 0;

	public static Arm64PowerMeterValues GetPowerMeterValues()
	{
		try
		{
			var powerValues = GetPowerValues();
			var totalCpuPowerInWatts = GetTotalCpuPowerInWatts(powerValues);
			var currentGpuPowerInWatts = GetPowerInWatts(powerValues, GpuName);
			var multimediaPowerInWatts = GetPowerInWatts(powerValues, MultimediaName);
			var systemPowerInWatts = GetPowerInWatts(powerValues, SystemName, SystemFallbackName);
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

	private static float? GetTotalCpuPowerInWatts(List<PowerValue> powerValues)
	{
		var cpuPowerValues = powerValues.Where(x => x.Name.StartsWith(CpuClusterNamePrefix, StringComparison.OrdinalIgnoreCase)).ToList();
		if (cpuPowerValues.Count == 0) return null;

		return ConvertMilliwattsToWatts(cpuPowerValues.Sum(x => x.PowerInMilliwatts));
	}

	private static float ConvertToSingle(object value)
	{
		if (value == null || value == DBNull.Value) return 0;
		return Convert.ToSingle(value, CultureInfo.InvariantCulture);
	}

	private static float? GetPowerInWatts(List<PowerValue> powerValues, params string[] names)
	{
		foreach (var name in names)
		{
			foreach (var powerValue in powerValues)
			{
				if (string.Equals(powerValue.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return ConvertMilliwattsToWatts(powerValue.PowerInMilliwatts);
				}
			}
		}

		return null;
	}

	private static float ConvertMilliwattsToWatts(float powerInMilliwatts) => powerInMilliwatts / MilliwattsPerWatt;

	private readonly record struct PowerValue(string Name, float PowerInMilliwatts);
}

public readonly record struct Arm64PowerMeterValues(float? TotalCpuPackagePower, float? CurrentGpuPower, float? MultimediaPower, float? SystemOnChipPower, float? SystemPower)
{
	public string GetCpuPowerInformationText()
	{
		var powerInformationTexts = new List<string>();
		AddPowerInformationText(powerInformationTexts, "CPU", TotalCpuPackagePower);
		AddPowerInformationText(powerInformationTexts, "SoC", SystemOnChipPower);
		AddPowerInformationText(powerInformationTexts, "Sys", SystemPower);
		return JoinPowerInformationTexts(powerInformationTexts);
	}

	public string GetGpuPowerInformationText()
	{
		var powerInformationTexts = new List<string>();
		AddPowerInformationText(powerInformationTexts, "GPU", CurrentGpuPower);
		AddPowerInformationText(powerInformationTexts, "MM", MultimediaPower);
		return JoinPowerInformationTexts(powerInformationTexts);
	}

	private static void AddPowerInformationText(List<string> powerInformationTexts, string label, float? powerInWatts)
	{
		if (powerInWatts.HasValue) powerInformationTexts.Add($"{label} {powerInWatts.Value:N0} W");
	}

	private static string JoinPowerInformationTexts(List<string> powerInformationTexts) => powerInformationTexts.Count == 0 ? string.Empty : " / " + string.Join(" / ", powerInformationTexts);
}
