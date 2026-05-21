namespace PinStats;

public readonly record struct UsageInformation(DateTime Time, int CpuUsage, int GpuUsage);

public static class UsageHistoryBuffer
{
	public static TimeSpan HistoryDuration { get; } = TimeSpan.FromSeconds(30);

	private static readonly object s_usageInformationLock = new();
	private static readonly List<UsageInformation> s_usageInformationHistory = [];

	public static event EventHandler<UsageInformation> UsageInformationAdded;

	public static UsageInformation[] GetSnapshot()
	{
		lock (s_usageInformationLock)
		{
			Trim(DateTime.Now);
			return [.. s_usageInformationHistory];
		}
	}

	public static void AddUsageInformation(int cpuUsage, int gpuUsage)
	{
		var usageInformation = new UsageInformation(DateTime.Now, cpuUsage, gpuUsage);

		lock (s_usageInformationLock)
		{
			s_usageInformationHistory.Add(usageInformation);
			Trim(usageInformation.Time);
		}

		UsageInformationAdded?.Invoke(null, usageInformation);
	}

	private static void Trim(DateTime now)
	{
		var minimumTime = now - HistoryDuration;
		var firstValidIndex = s_usageInformationHistory.FindIndex(usageInformation => usageInformation.Time >= minimumTime);
		if (firstValidIndex < 0)
		{
			s_usageInformationHistory.Clear();
			return;
		}
		if (firstValidIndex > 0) s_usageInformationHistory.RemoveRange(0, firstValidIndex);
	}
}
