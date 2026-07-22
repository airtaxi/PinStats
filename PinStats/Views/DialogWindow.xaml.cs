using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PinStats.Helpers;
using System.Windows.Input;
using WinUIEx;
using Windows.Graphics;

namespace PinStats.Views;

// Shared dialog window used instead of DevWinUI's WindowedContentDialog, whose buttons always close the window.
// The secondary button only executes its command without closing, so actions like "reset to default" can keep the dialog open.
// WindowEx is not a DependencyObject, so the button properties are simple pass-through properties to the named elements instead of dependency properties.
// Auto-sizing mirrors DevWinUI's ContentWindow.SizeToContent: on Loaded, the root Grid's DesiredSize is measured and AppWindow.ResizeClient is called
// so the window fits its content without a hardcoded Width/Height. Callers may still set Width/Height to override the auto-measured size.
public sealed partial class DialogWindow : WindowEx
{
	public object Header
	{
		get => HeaderContentControl.Content;
		set => HeaderContentControl.Content = value;
	}

	public new object Content
	{
		get => DialogContentPresenter.Content;
		set => DialogContentPresenter.Content = value;
	}

	public object PrimaryButtonContent
	{
		get => PrimaryButton.Content;
		set
		{
			PrimaryButton.Content = value;
			PrimaryButton.Visibility = GetButtonVisibility(value);
			UpdateButtonColumns();
		}
	}

	public object SecondaryButtonContent
	{
		get => SecondaryButton.Content;
		set
		{
			SecondaryButton.Content = value;
			SecondaryButton.Visibility = GetButtonVisibility(value);
			UpdateButtonColumns();
		}
	}

	public object CloseButtonContent
	{
		get => CloseButton.Content;
		set
		{
			CloseButton.Content = value;
			CloseButton.Visibility = GetButtonVisibility(value);
			UpdateButtonColumns();
		}
	}

	public bool IsPrimaryButtonEnabled
	{
		get => PrimaryButton.IsEnabled;
		set => PrimaryButton.IsEnabled = value;
	}

	// The secondary button executes this command without closing the dialog; its enabled state follows the command's CanExecute.
	public ICommand SecondaryButtonCommand
	{
		get => SecondaryButton.Command;
		set => SecondaryButton.Command = value;
	}

	public ContentDialogResult Result { get; private set; } = ContentDialogResult.None;

	// When set to a positive value, overrides the auto-measured width. NaN means auto-size from content.
	public new double Width { get; set; } = double.NaN;

	// When set to a positive value, overrides the auto-measured height. NaN means auto-size from content.
	public new double Height { get; set; } = double.NaN;

	public DialogWindow()
	{
		InitializeComponent();

		SystemBackdrop = new MicaBackdrop();
		AppWindow.IsShownInSwitchers = false;
        (AppWindow.Presenter as OverlappedPresenter).SetBorderAndTitleBar(true, false);
        ExtendsContentIntoTitleBar = true;
		this.SetIsAlwaysOnTop(true);

		// Fix white flickering issue when the window is first shown
		if ((base.Content as FrameworkElement).RequestedTheme == ElementTheme.Dark) WindowHelper.SetDarkModeWindow(this);

		RootGrid.Loaded += OnRootGridLoaded;
	}

	public Task<ContentDialogResult> ShowDialogAsync()
	{
		var resultCompletionSource = new TaskCompletionSource<ContentDialogResult>();
		Closed += (_, _) => resultCompletionSource.TrySetResult(Result);
		this.CenterOnScreen();
		Activate();
		return resultCompletionSource.Task;
	}

	private void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
	{
		Result = ContentDialogResult.Primary;
		Close();
	}

	private void OnCloseButtonClick(object sender, RoutedEventArgs e) => Close();

	private void OnEscapeKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
	{
		e.Handled = true;
		Close();
	}

	// Collapse the column when its button is hidden so the visible buttons share the full width evenly, matching DevWinUI's UniformStackPanel behavior.
	private void UpdateButtonColumns()
	{
		PrimaryButtonColumn.Width = PrimaryButton.Visibility == Visibility.Visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
		SecondaryButtonColumn.Width = SecondaryButton.Visibility == Visibility.Visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
		CloseButtonColumn.Width = CloseButton.Visibility == Visibility.Visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
	}

	private static Visibility GetButtonVisibility(object content) => content is string text ? (!string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed) : (content != null ? Visibility.Visible : Visibility.Collapsed);

	// Mirror DevWinUI ContentWindow.OnLoaded + ResizeToContent: measure the root Grid's DesiredSize and resize the window's client area to fit.
	// ExtendsContentIntoTitleBar is true, so the title bar height (30px in DIP) is subtracted from the measured height before calling ResizeClient,
	// matching DevWinUI's ContentWindow.Resize behavior.
	private void OnRootGridLoaded(object sender, RoutedEventArgs e)
	{
		RootGrid.Loaded -= OnRootGridLoaded;
		RootGrid.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

		var dpiScale = RootGrid.XamlRoot.RasterizationScale;
		var desiredWidth = double.IsNormal(Width) && double.IsPositive(Width) ? Width : RootGrid.DesiredSize.Width;
		var desiredHeight = double.IsNormal(Height) && double.IsPositive(Height) ? Height : RootGrid.DesiredSize.Height;
		if (ExtendsContentIntoTitleBar) desiredHeight -= 30;

		AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(desiredWidth * dpiScale), (int)Math.Ceiling(desiredHeight * dpiScale)));
		this.CenterOnScreen();
	}
}