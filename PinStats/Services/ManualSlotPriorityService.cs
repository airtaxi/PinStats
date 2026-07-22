using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PinStats.Views;

namespace PinStats.Services;

public sealed class ManualSlotPriorityService(LocalizationService localizationService)
{
	public async Task ShowAndApplyAsync()
	{
		var textBox = new TextBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Text = TaskbarWidgetSettings.ManualSlotPriority.ToString()
		};
		textBox.BeforeTextChanging += (_, e) => e.Cancel = e.NewText.Any(character => !char.IsDigit(character));

		var dialog = new DialogWindow
		{
			Header = localizationService.GetLocalizedString("Dialog.ManualSlotPriorityTitle"),
			PrimaryButtonContent = localizationService.GetLocalizedString("Dialog.Ok"),
			CloseButtonContent = localizationService.GetLocalizedString("Dialog.Cancel"),
			Content = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					new TextBlock
					{
						Text = localizationService.GetLocalizedString("Dialog.ManualSlotPriorityDescription"),
						TextWrapping = TextWrapping.Wrap,
						MaxWidth = 420
					},
					textBox
				}
			}
		};

		// The primary button is only enabled when the input is a valid ushort value.
		textBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = ushort.TryParse(textBox.Text, out _);

		var result = await dialog.ShowDialogAsync();
		if (result != ContentDialogResult.Primary) return;
		if (!ushort.TryParse(textBox.Text, out var manualSlotPriority)) return;
		if (manualSlotPriority == TaskbarWidgetSettings.ManualSlotPriority) return;

		TaskbarWidgetSettings.ManualSlotPriority = manualSlotPriority;
		App.RelaunchTaskbarWidgetWindow();
	}
}
