using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace PinStats.ViewModels;

public class TotalUsageViewModel : ObservableObject
{
    public string DataLabelText { get; set; }

    public ISeries[] Series { get; set; }
    public Axis[] XAxes { get; set; } = [new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = false }];
    public Axis[] YAxes { get; set; } = [new Axis { IsVisible = false }];

    public TotalUsageViewModel()
    {
        Series =
        [
            new RowSeries<float>
            {
                IsHoverable = false,
                Values = [100],
                Stroke = null,
                Fill = new SolidColorPaint(new SKColor(128, 128, 128, 128)),
                IgnoresBarPosition = true
            },
            new RowSeries<float>
            {
                IsHoverable = false,
                Values = [0],
                Stroke = null,
                Fill = new SolidColorPaint(SKColors.CornflowerBlue),
                IgnoresBarPosition = true
            },
            new RowSeries<float>
            {
                IsHoverable=false,
                Values=[100],
                Fill = null,
                Stroke = null,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
                DataLabelsFormatter = (point) => DataLabelText,
                IgnoresBarPosition = true
            }
        ];
    }

    public void SetValue(float max, float current, string dataLabelText = null)
    {
        DataLabelText = dataLabelText;
        XAxes[0].MaxLimit = max;
        Series[0].Values = new float[] { max };
        Series[1].Values = new float[] { current };
        Series[2].Values = new float[] { max };
    }
}