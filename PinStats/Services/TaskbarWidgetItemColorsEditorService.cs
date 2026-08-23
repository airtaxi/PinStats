using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using PinStats.Controls;
using PinStats.Messages;
using PinStats.ViewModels;
using PinStats.Views;

namespace PinStats.Services;

public sealed class TaskbarWidgetItemColorsEditorService(LocalizationService localizationService)
{
	public async Task ShowAndApplyAsync()
	{
		var viewModel = new TaskbarWidgetItemColorsEditorViewModel(localizationService);
		var dialog = new DialogWindow
		{
			Header = localizationService.GetLocalizedString("Dialog.TaskbarWidgetItemColorsTitle"),
			PrimaryButtonContent = localizationService.GetLocalizedString("Dialog.Ok"),
			SecondaryButtonContent = localizationService.GetLocalizedString("Dialog.ResetToDefault"),
			SecondaryButtonCommand = viewModel.ResetToDefaultCommand,
			CloseButtonContent = localizationService.GetLocalizedString("Dialog.Cancel"),
			Content = new TaskbarWidgetItemColorsEditorControl(viewModel),
			Width = 560,
		};

		var result = await dialog.ShowDialogAsync();
		if (result != ContentDialogResult.Primary) return;
		if (!viewModel.ApplyChanges()) return;

		WeakReferenceMessenger.Default.Send(new TaskbarWidgetItemColorChangedMessage());
	}
}
