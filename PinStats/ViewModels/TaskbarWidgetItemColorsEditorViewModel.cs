using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PinStats.Enums;
using PinStats.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetItemColorsEditorViewModel : ObservableObject
{
	public string DescriptionText { get; }

	public ObservableCollection<TaskbarWidgetEditableColorItemViewModel> Items { get; } = [];

	public TaskbarWidgetItemColorsEditorViewModel(LocalizationService localizationService)
	{
		DescriptionText = localizationService.GetLocalizedString("Dialog.TaskbarWidgetItemColorsDescription");

		var hasBattery = HardwareMonitor.HasBattery();
		foreach (var itemType in TaskbarWidgetSettings.GetItemOrder())
		{
			if (!IsPercentItemType(itemType)) continue;

			// Battery percent item is hidden on devices without a battery, following the same rule as the items editor.
			if (!hasBattery && itemType == TaskbarWidgetItemType.BatteryPercent) continue;

			var displayName = localizationService.GetLocalizedString($"TaskbarWidgetItem.{itemType}");
			var item = new TaskbarWidgetEditableColorItemViewModel(itemType, displayName);
			item.PropertyChanged += OnItemPropertyChanged;
			Items.Add(item);
		}
	}

	private static bool IsPercentItemType(TaskbarWidgetItemType itemType) => itemType is TaskbarWidgetItemType.CpuUsage or TaskbarWidgetItemType.GpuUsage or TaskbarWidgetItemType.MemoryUsage or TaskbarWidgetItemType.VirtualMemoryUsage or TaskbarWidgetItemType.BatteryPercent;

	private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TaskbarWidgetEditableColorItemViewModel.IsCustomColor))
		{
			ResetToDefaultCommand.NotifyCanExecuteChanged();
		}
	}

	[RelayCommand(CanExecute = nameof(CanResetToDefault))]
	private void ResetToDefault()
	{
		foreach (var item in Items) item.IsCustomColor = false;
		ResetToDefaultCommand.NotifyCanExecuteChanged();
	}

	private bool CanResetToDefault() => Items.Any(item => item.IsCustomColor);

	public bool ApplyChanges()
	{
		var hasChanges = false;
		foreach (var item in Items)
		{
			var newColorHex = item.IsCustomColor ? TaskbarWidgetSettings.ToItemColorHex(item.SelectedColor) : null;
			if (newColorHex == TaskbarWidgetSettings.GetItemColorHex(item.ItemType)) continue;

			TaskbarWidgetSettings.SetItemColorHex(item.ItemType, newColorHex);
			hasChanges = true;
		}
		return hasChanges;
	}
}
