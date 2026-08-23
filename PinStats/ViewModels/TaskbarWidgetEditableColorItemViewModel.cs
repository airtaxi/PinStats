using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PinStats.Enums;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace PinStats.ViewModels;

public partial class TaskbarWidgetEditableColorItemViewModel : ObservableObject
{
	private static readonly UISettings s_uiSettings = new();

	// Guards programmatic SelectedColor assignments (initial value, color reset) from being treated as a user color selection.
	private bool _isApplyingProgrammaticColor;

	public TaskbarWidgetItemType ItemType { get; }
	public string DisplayName { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SwatchBrush))]
	public partial Color SelectedColor { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SwatchBrush))]
	[NotifyPropertyChangedFor(nameof(IsResetColorButtonVisible))]
	public partial bool IsCustomColor { get; set; }

	public Brush SwatchBrush => IsCustomColor ? new SolidColorBrush(SelectedColor) : GetDefaultSwatchBrush();

	public Visibility IsResetColorButtonVisible => IsCustomColor ? Visibility.Visible : Visibility.Collapsed;

	public TaskbarWidgetEditableColorItemViewModel(TaskbarWidgetItemType itemType, string displayName)
	{
		ItemType = itemType;
		DisplayName = displayName;
		IsCustomColor = TaskbarWidgetSettings.TryGetItemColor(itemType, out var storedColor);
		_isApplyingProgrammaticColor = true;
		SelectedColor = IsCustomColor ? storedColor : GetDefaultColor();
		_isApplyingProgrammaticColor = false;
	}

	// Picking a color in the ColorPicker immediately enables the custom color state so the swatch and reset button update.
	partial void OnSelectedColorChanged(Color value)
	{
		if (_isApplyingProgrammaticColor) return;
		IsCustomColor = true;
	}

	[RelayCommand]
	private void ResetColor()
	{
		IsCustomColor = false;
		_isApplyingProgrammaticColor = true;
		SelectedColor = GetDefaultColor();
		_isApplyingProgrammaticColor = false;
	}

	private static SolidColorBrush GetDefaultSwatchBrush()
	{
		if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var resource) && resource is SolidColorBrush solidColorBrush) return solidColorBrush;
		return new SolidColorBrush(s_uiSettings.GetColorValue(UIColorType.Accent));
	}

	// The picker starts at the current accent color so the preview matches the default ProgressRing foreground.
	private static Color GetDefaultColor() => GetDefaultSwatchBrush().Color;
}
