﻿using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PinStats.ViewModels;

public class UsageViewModel : ObservableObject
{
	private readonly ObservableCollection<DateTimePoint> _values = [];
	private readonly DateTimeAxis _customAxis;

	public ObservableCollection<ISeries> Series { get; }
	public IEnumerable<ICartesianAxis> XAxes { get; }
	public IEnumerable<ICartesianAxis> YAxes { get; }
	public object Sync { get; set; } = new object();
	public bool IsReading { get; set; } = true;

	public UsageViewModel()
	{
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

	public void RefreshSync() => Sync = new object();

	private static double[] GetSeparators()
	{
		var now = DateTime.Now;

		var seperators = new List<double> { now.Ticks };
		for (int i = 0; i < 20; i++)
		{
			seperators.Add(now.AddSeconds(-i * 5).Ticks);
		}

		seperators.Reverse();
		return [.. seperators];
	}

	private static string Formatter(DateTime date)
	{
		var secsAgo = (DateTime.Now - date).TotalSeconds;

		return secsAgo < 1
			? "now"
			: $"{secsAgo:N0}s";
	}

	public void AddUsageInformation(int percent)
	{
		lock (Sync)
		{
			_values.Add(new DateTimePoint(DateTime.Now, percent));
			while (_values.Count > 100) _values.RemoveAt(0);

			// we need to update the separators every time we add a new point 
			_customAxis.CustomSeparators = GetSeparators();
		}
	}
}
