using Microsoft.UI.Xaml.Controls;
using PinStats.ViewModels;

namespace PinStats.Controls;

public sealed partial class TaskbarWidgetItemColorsEditorControl : UserControl
{
	public TaskbarWidgetItemColorsEditorViewModel ViewModel { get; }

	public TaskbarWidgetItemColorsEditorControl(TaskbarWidgetItemColorsEditorViewModel viewModel)
	{
		// Assign the view model before InitializeComponent so that x:Bind expressions can resolve it.
		ViewModel = viewModel;
		InitializeComponent();
	}
}
