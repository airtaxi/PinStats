using Microsoft.UI.Xaml.Controls;
using PinStats.Controls;
using PinStats.ViewModels;
using PinStats.Views;

namespace PinStats.Services;

public sealed class TaskbarWidgetItemsEditorService(LocalizationService localizationService)
{
	public async Task ShowAndApplyAsync()
	{
		var viewModel = new TaskbarWidgetItemsEditorViewModel(localizationService);
		var dialog = new DialogWindow
		{
			Header = localizationService.GetLocalizedString("Dialog.TaskbarWidgetItemsEditorTitle"),
			PrimaryButtonContent = localizationService.GetLocalizedString("Dialog.Ok"),
			SecondaryButtonContent = localizationService.GetLocalizedString("Dialog.ResetToDefault"),
			SecondaryButtonCommand = viewModel.ResetToDefaultCommand,
			CloseButtonContent = localizationService.GetLocalizedString("Dialog.Cancel"),
			Content = new TaskbarWidgetItemsEditorControl(viewModel),
            Width = 560,
        };

		var result = await dialog.ShowDialogAsync();
		if (result != ContentDialogResult.Primary) return;
		if (!viewModel.ApplyChanges()) return;

		App.RelaunchTaskbarWidgetWindow();
	}
}
