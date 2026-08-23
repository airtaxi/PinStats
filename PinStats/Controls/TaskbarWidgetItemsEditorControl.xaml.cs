using Microsoft.UI.Xaml.Controls;
using PinStats.ViewModels;

namespace PinStats.Controls;

public sealed partial class TaskbarWidgetItemsEditorControl : UserControl
{
	public TaskbarWidgetItemsEditorViewModel ViewModel { get; }

	public TaskbarWidgetItemsEditorControl(TaskbarWidgetItemsEditorViewModel viewModel)
	{
		// Assign the view model before InitializeComponent so that x:Bind expressions can resolve it.
		ViewModel = viewModel;
		InitializeComponent();
	}
}
