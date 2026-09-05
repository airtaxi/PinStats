using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using HidSharp.Reports.Encodings;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using PinStats.Messages;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetItemViewModel : ObservableObject, IRecipient<TaskbarWidgetItemColorChangedMessage>
{
    public TaskbarWidgetItemType ItemType { get; }
    public double Width { get; }
    public string PrimaryGlyph { get; }
    public string SecondaryGlyph { get; }

    // Null when no custom color is configured; the widget template falls back to the default accent brush.
    public Brush Foreground => TaskbarWidgetSettings.TryGetItemColor(ItemType, out var color) ? new SolidColorBrush(color) : null;

    [ObservableProperty]
    public partial double Value { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial string PrimaryText { get; set; } = "0 KB/s";

    [ObservableProperty]
    public partial string SecondaryText { get; set; } = "0 KB/s";

    [ObservableProperty]
    public partial double ProgressRingSize { get; set; } = 36;

    [ObservableProperty]
    public partial double FontSize { get; set; } = 10;

    public TaskbarWidgetItemViewModel(TaskbarWidgetItemType itemType)
    {
        ItemType = itemType;
        Width = TaskbarWidgetSettings.GetItemWidth(itemType);
        PrimaryGlyph = GetPrimaryGlyph(itemType);
        SecondaryGlyph = GetSecondaryGlyph(itemType);
        Text = itemType == TaskbarWidgetItemType.BatteryPower ? "+0.0 W" : "0%";

        WeakReferenceMessenger.Default.Register(this);
    }

    private static string GetPrimaryGlyph(TaskbarWidgetItemType itemType) => itemType switch
    {
        TaskbarWidgetItemType.NetworkSpeed => "\uE74A",
        TaskbarWidgetItemType.StorageSpeed => "\uE896",
        _ => string.Empty
    };

    private static string GetSecondaryGlyph(TaskbarWidgetItemType itemType) => itemType switch
    {
        TaskbarWidgetItemType.NetworkSpeed => "\uE74B",
        TaskbarWidgetItemType.StorageSpeed => "\uE898",
        _ => string.Empty
    };

    public void Receive(TaskbarWidgetItemColorChangedMessage message) => OnPropertyChanged(nameof(Foreground));
}
