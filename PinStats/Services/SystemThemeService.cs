using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace PinStats.Services;

public sealed class SystemThemeService
{
	private readonly UISettings _uiSettings = new();

	private ElementTheme _currentSystemTheme;

	public ElementTheme CurrentSystemTheme => _currentSystemTheme;

	public bool IsSystemLightTheme => _currentSystemTheme == ElementTheme.Light;

	public event EventHandler<SystemThemeChangedEventArgs> SystemThemeChanged;

	public SystemThemeService()
	{
		_currentSystemTheme = DetectSystemTheme();
		_uiSettings.ColorValuesChanged += OnUiSettingsColorValuesChanged;
	}

	public ElementTheme DetectSystemTheme()
	{
		using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
		return key?.GetValue("SystemUsesLightTheme") is int value && value != 0 ? ElementTheme.Light : ElementTheme.Dark;
	}

	private void OnUiSettingsColorValuesChanged(UISettings sender, object args)
	{
		var detectedTheme = DetectSystemTheme();
		if (_currentSystemTheme == detectedTheme) return;

		_currentSystemTheme = detectedTheme;
		SystemThemeChanged?.Invoke(this, new SystemThemeChangedEventArgs(detectedTheme));
	}
}

public sealed class SystemThemeChangedEventArgs(ElementTheme systemTheme) : EventArgs
{
	public ElementTheme SystemTheme { get; } = systemTheme;
}