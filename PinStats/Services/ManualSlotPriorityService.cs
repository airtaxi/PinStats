using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowedContentDialog = DevWinUI.WindowedContentDialog;

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

		var dialog = new WindowedContentDialog
		{
			Header = localizationService.GetLocalizedString("Dialog.ManualSlotPriorityTitle"),
			PrimaryButtonContent = localizationService.GetLocalizedString("Dialog.Ok"),
			SecondaryButtonContent = localizationService.GetLocalizedString("Dialog.Cancel"),
			DefaultButton = ContentDialogButton.Primary,
			CanResize = false,
			IsPrimaryButtonEnabled = true,
			IsSecondaryButtonEnabled = true,
			Content = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					new TextBlock
					{
						Text = localizationService.GetLocalizedString("Dialog.ManualSlotPriorityDescription"),
						TextWrapping = TextWrapping.Wrap
					},
					textBox
				}
			}
		};

		// The primary button is only enabled when the input is a valid ushort value.
		textBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = ushort.TryParse(textBox.Text, out _);

		var result = await dialog.ShowAsync();
		if (result != ContentDialogResult.Primary) return;
		if (!ushort.TryParse(textBox.Text, out var manualSlotPriority)) return;
		if (manualSlotPriority == TaskbarWidgetSettings.ManualSlotPriority) return;

		TaskbarWidgetSettings.ManualSlotPriority = manualSlotPriority;
		App.RelaunchTaskbarWidgetWindow();
	}
}
