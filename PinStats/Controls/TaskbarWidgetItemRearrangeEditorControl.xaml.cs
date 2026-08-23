using Microsoft.UI.Xaml.Controls;
using PinStats.ViewModels;

namespace PinStats.Controls;

public sealed partial class TaskbarWidgetItemRearrangeEditorControl : UserControl
{
	public TaskbarWidgetItemRearrangeEditorViewModel ViewModel { get; }

	public TaskbarWidgetItemRearrangeEditorControl(TaskbarWidgetItemRearrangeEditorViewModel viewModel)
	{
		// Assign the view model before InitializeComponent so that x:Bind expressions can resolve it.
		ViewModel = viewModel;
		InitializeComponent();
	}
}
