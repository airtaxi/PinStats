using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PinStats.Enums;
using PinStats.ViewModels;

namespace PinStats.Controls;

public class TaskbarWidgetItemTemplateSelector : DataTemplateSelector
{
	public DataTemplate PercentItemTemplate { get; set; }
	public DataTemplate SpeedItemTemplate { get; set; }
	public DataTemplate BatteryPowerItemTemplate { get; set; }

	protected override DataTemplate SelectTemplateCore(object item) => item is TaskbarWidgetItemViewModel itemViewModel ? SelectTemplate(itemViewModel.ItemType) : PercentItemTemplate;

	private DataTemplate SelectTemplate(TaskbarWidgetItemType itemType) => itemType switch
	{
		TaskbarWidgetItemType.NetworkSpeed or TaskbarWidgetItemType.StorageSpeed => SpeedItemTemplate,
		TaskbarWidgetItemType.BatteryPower => BatteryPowerItemTemplate,
		_ => PercentItemTemplate
	};
}
