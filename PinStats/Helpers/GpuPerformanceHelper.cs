using System.Runtime.InteropServices;

namespace PinStats.Helpers;

/// <summary>
/// Provides efficient GPU utilization reading via the Windows PDH (Performance Data Helper) API.
/// Uses a single wildcard query to batch-read all GPU 3D engine counters in one system call,
/// avoiding the overhead of individual PerformanceCounter.NextValue() calls.
/// </summary>
public static partial class GpuPerformanceHelper
{
	private const string GpuEngineCounterPath = @"\GPU Engine(*engtype_3D*)\Utilization Percentage";
	private const uint PdhFormatDouble = 0x00000200;
	private const int PdhMoreData = unchecked((int)0x800007D2);
	private const int RefreshIntervalInSeconds = 10;

	private static IntPtr s_query;
	private static IntPtr s_counter;
	private static bool s_initialized;
	private static DateTime s_refreshedAt = DateTime.MinValue;

	[StructLayout(LayoutKind.Explicit, Size = 16)]
	private struct PdhFormattedCounterValue
	{
		[FieldOffset(0)]
		public uint CStatus;
		[FieldOffset(8)]
		public double DoubleValue;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PdhFormattedCounterValueItem
	{
		public IntPtr Name;
		public PdhFormattedCounterValue FormattedValue;
	}

	[LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
	private static partial int PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

	[LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

	[LibraryImport("pdh.dll")]
	private static partial int PdhCollectQueryData(IntPtr query);

	[LibraryImport("pdh.dll")]
	private static partial int PdhCloseQuery(IntPtr query);

	[LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref int bufferSize, out int itemCount, IntPtr itemBuffer);

	private static void Initialize()
	{
		if (s_query != IntPtr.Zero)
		{
            _ = PdhCloseQuery(s_query);
			s_query = IntPtr.Zero;
			s_counter = IntPtr.Zero;
			s_initialized = false;
		}

		if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out s_query) != 0) return;

		if (PdhAddEnglishCounter(s_query, GpuEngineCounterPath, IntPtr.Zero, out s_counter) != 0)
		{
            _ = PdhCloseQuery(s_query);
			s_query = IntPtr.Zero;
			return;
		}

        // First collect primes the rate counter (first value is always 0).
        _ = PdhCollectQueryData(s_query);
		s_initialized = true;
		s_refreshedAt = DateTime.Now;
	}

	/// <summary>
	/// Returns the total GPU 3D engine utilization percentage by reading all matching
	/// GPU Engine performance counters in a single PDH query.
	/// Periodically refreshes the query to pick up new/removed GPU engine instances.
	/// </summary>
	public static float GetTotalUtilization()
	{
		if (DateTime.Now - s_refreshedAt > TimeSpan.FromSeconds(RefreshIntervalInSeconds))
		{
			try { Initialize(); }
			catch (Exception exception) { App.WriteException(exception); }
		}

		if (!s_initialized) return 0;

		try
		{
			if (PdhCollectQueryData(s_query) != 0) return 0;

			int bufferSize = 0;
            int status = PdhGetFormattedCounterArray(s_counter, PdhFormatDouble, ref bufferSize, out int itemCount, IntPtr.Zero);
            if (status != PdhMoreData || bufferSize == 0) return 0;

			var buffer = Marshal.AllocHGlobal(bufferSize);
			try
			{
				status = PdhGetFormattedCounterArray(s_counter, PdhFormatDouble, ref bufferSize, out itemCount, buffer);
				if (status != 0) return 0;

				int itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
				double totalUtilization = 0;

				for (int i = 0; i < itemCount; i++)
				{
					var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(buffer + i * itemSize);
					if (item.FormattedValue.CStatus == 0) totalUtilization += item.FormattedValue.DoubleValue;
				}

				return (float)totalUtilization;
			}
			finally { Marshal.FreeHGlobal(buffer); }
		}
		catch (Exception exception)
		{
			App.WriteException(exception);
			return 0;
		}
	}
}
