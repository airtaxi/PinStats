using CommunityToolkit.Mvvm.ComponentModel;
using PinStats.Enums;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetItemViewModel(TaskbarWidgetItemType itemType) : ObservableObject
{
    public TaskbarWidgetItemType ItemType { get; } = itemType;
    public double Width { get; } = TaskbarWidgetSettings.GetItemWidth(itemType);
    public string PrimaryGlyph { get; } = GetPrimaryGlyph(itemType);
    public string SecondaryGlyph { get; } = GetSecondaryGlyph(itemType);

    [ObservableProperty]
	public partial double Value { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = itemType == TaskbarWidgetItemType.BatteryPower ? "+0.0 W" : "0%";

    [ObservableProperty]
    public partial string PrimaryText { get; set; } = "0 KB/s";

    [ObservableProperty]
    public partial string SecondaryText { get; set; } = "0 KB/s";

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
}
