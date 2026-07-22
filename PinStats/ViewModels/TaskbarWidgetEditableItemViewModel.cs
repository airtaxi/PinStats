using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PinStats.Enums;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetEditableItemViewModel(TaskbarWidgetItemType itemType, string displayName, bool isEnabled, Action<TaskbarWidgetEditableItemViewModel, int> requestMoveAction) : ObservableObject
{
	public TaskbarWidgetItemType ItemType { get; } = itemType;

	public string DisplayName { get; } = displayName;

	[ObservableProperty]
	public partial bool IsEnabled { get; set; } = isEnabled;

	[ObservableProperty]
	public partial bool CanMoveUp { get; set; }

	[ObservableProperty]
	public partial bool CanMoveDown { get; set; }

	[RelayCommand]
	private void MoveUp() => requestMoveAction(this, -1);

	[RelayCommand]
	private void MoveDown() => requestMoveAction(this, 1);
}
