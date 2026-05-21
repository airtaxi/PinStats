using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PinStats.Enums;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PinStats.ViewModels;

public partial class UsageViewModel : ObservableObject
{
	private readonly UsageHistoryMetric _usageHistoryMetric;
	private readonly ObservableCollection<DateTimePoint> _values = [];
	private readonly DateTimeAxis _customAxis;

	public ObservableCollection<ISeries> Series { get; }
	public IEnumerable<ICartesianAxis> XAxes { get; }
	public IEnumerable<ICartesianAxis> YAxes { get; }
	public bool IsReading { get; set; } = true;

	public UsageViewModel(UsageHistoryMetric usageHistoryMetric)
	{
		_usageHistoryMetric = usageHistoryMetric;
		Series =
		[
			new LineSeries<DateTimePoint>
			{
				Values = _values,
				Fill = null,
				GeometryFill = null,
				GeometryStroke = null,
			}
		];

		_customAxis = new DateTimeAxis(TimeSpan.FromSeconds(1), Formatter)
		{
			CustomSeparators = GetSeparators(),
			AnimationsSpeed = TimeSpan.FromMilliseconds(0),
			SeparatorsPaint = new SolidColorPaint(SKColors.Black.WithAlpha(100))
		};

		XAxes = [_customAxis];
		YAxes = [
			new Axis {
				MaxLimit = 100,
				MinLimit = 0
			}
		];
	}

	private static double[] GetSeparators()
	{
		var now = DateTime.Now;

		var separators = new List<double>();
		for (var secondsAgo = (int)UsageHistoryBuffer.HistoryDuration.TotalSeconds; secondsAgo >= 0; secondsAgo -= 5) separators.Add(now.AddSeconds(-secondsAgo).Ticks);

		return [.. separators];
	}

	private static string Formatter(DateTime date)
	{
		var secondsAgo = (DateTime.Now - date).TotalSeconds;

		return secondsAgo < 1 ? "now" : $"{secondsAgo:N0}s";
	}

	public void LoadUsageInformation(IEnumerable<UsageInformation> usageInformationHistory)
	{
		_values.Clear();
		foreach (var usageInformation in usageInformationHistory) _values.Add(CreateDateTimePoint(usageInformation));

		TrimUsageInformation(DateTime.Now);
		_customAxis.CustomSeparators = GetSeparators();
	}

	public void AddUsageInformation(UsageInformation usageInformation)
	{
		_values.Add(CreateDateTimePoint(usageInformation));
		TrimUsageInformation(usageInformation.Time);

		// we need to update the separators every time we add a new point
		_customAxis.CustomSeparators = GetSeparators();
	}

	private DateTimePoint CreateDateTimePoint(UsageInformation usageInformation) => new(usageInformation.Time, GetUsage(usageInformation));

	private int GetUsage(UsageInformation usageInformation) => _usageHistoryMetric switch
	{
		UsageHistoryMetric.CpuUsage => usageInformation.CpuUsage,
		UsageHistoryMetric.GpuUsage => usageInformation.GpuUsage,
		_ => 0
	};

	private void TrimUsageInformation(DateTime now)
	{
		var minimumTime = now - UsageHistoryBuffer.HistoryDuration;
		while (_values.Count > 0 && _values[0].DateTime < minimumTime) _values.RemoveAt(0);
	}
}
