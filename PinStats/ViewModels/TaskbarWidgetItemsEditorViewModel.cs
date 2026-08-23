using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PinStats.Enums;
using PinStats.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetItemsEditorViewModel : ObservableObject
{
	private static readonly TaskbarWidgetItemType[] s_batteryItemTypes = [TaskbarWidgetItemType.BatteryPercent, TaskbarWidgetItemType.BatteryPower];

	// Battery items hidden on devices without a battery keep their stored relative order and are appended after the edited items when applied.
	private readonly List<TaskbarWidgetItemType> _hiddenItemTypes = [];

	public ObservableCollection<TaskbarWidgetEditableItemViewModel> Items { get; } = [];

	public TaskbarWidgetItemsEditorViewModel(LocalizationService localizationService)
	{
		var hasBattery = HardwareMonitor.HasBattery();
		foreach (var itemType in TaskbarWidgetSettings.GetItemOrder())
		{
			if (!hasBattery && s_batteryItemTypes.Contains(itemType))
			{
				_hiddenItemTypes.Add(itemType);
				continue;
			}

			var displayName = localizationService.GetLocalizedString($"TaskbarWidgetItem.{itemType}");
			var item = new TaskbarWidgetEditableItemViewModel(itemType, displayName, TaskbarWidgetSettings.IsItemEnabled(itemType), MoveItem);
			item.PropertyChanged += OnItemPropertyChanged;
			Items.Add(item);
		}
		RefreshMoveStates();
	}

	// Native drag and drop based reordering is unavailable since the app always runs elevated, so items are moved with explicit up/down commands instead.
	private void MoveItem(TaskbarWidgetEditableItemViewModel item, int direction)
	{
		var currentIndex = Items.IndexOf(item);
		var newIndex = currentIndex + direction;
		if (currentIndex < 0 || newIndex < 0 || newIndex >= Items.Count) return;

		Items.Move(currentIndex, newIndex);
		RefreshMoveStates();
		ResetToDefaultCommand.NotifyCanExecuteChanged();
	}

	private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TaskbarWidgetEditableItemViewModel.IsEnabled))
		{
		    ResetToDefaultCommand.NotifyCanExecuteChanged();
		}
	}

	private void RefreshMoveStates()
	{
		for (var index = 0; index < Items.Count; index++)
		{
			Items[index].CanMoveUp = index > 0;
			Items[index].CanMoveDown = index < Items.Count - 1;
		}
	}

	// Only the visible items are reset; hidden battery items keep their stored state and are merged back when applied.
	[RelayCommand(CanExecute = nameof(CanResetToDefault))]
	private void ResetToDefault()
	{
		var remainingItems = Items.ToList();
		Items.Clear();
		foreach (var itemType in TaskbarWidgetSettings.GetDefaultItemOrder())
		{
			var item = remainingItems.FirstOrDefault(candidate => candidate.ItemType == itemType);
			if (item == null) continue;

			item.IsEnabled = TaskbarWidgetSettings.IsItemDefaultEnabled(itemType);
			Items.Add(item);
		}
		RefreshMoveStates();
		ResetToDefaultCommand.NotifyCanExecuteChanged();
	}

	private bool CanResetToDefault()
	{
		var itemTypes = Items.Select(item => item.ItemType).ToList();
		var isDefaultOrder = itemTypes.SequenceEqual(TaskbarWidgetSettings.GetDefaultItemOrder().Where(itemTypes.Contains));
		return !isDefaultOrder || Items.Any(item => item.IsEnabled != TaskbarWidgetSettings.IsItemDefaultEnabled(item.ItemType));
	}

	public bool ApplyChanges()
	{
		var currentItemOrder = TaskbarWidgetSettings.GetItemOrder();
		var newItemOrder = Items.Select(item => item.ItemType).ToList();
		foreach (var hiddenItemType in _hiddenItemTypes) newItemOrder.Add(hiddenItemType);

		var isOrderChanged = !newItemOrder.SequenceEqual(currentItemOrder);
		var isEnabledChanged = Items.Any(item => item.IsEnabled != TaskbarWidgetSettings.IsItemEnabled(item.ItemType));
		if (!isOrderChanged && !isEnabledChanged) return false;

		TaskbarWidgetSettings.SetItemOrder(newItemOrder);
		foreach (var item in Items) TaskbarWidgetSettings.SetItemEnabled(item.ItemType, item.IsEnabled);
		return true;
	}
}
