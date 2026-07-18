using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PinStats.Services;

public sealed class LocalizationService
{
	private static readonly List<string> s_supportedLanguageTags = ["en-US", "ko-KR", "ja-JP", "zh-Hans", "zh-Hant"];

	private ResourceLoader _resourceLoader = null!;

	public event Action LanguageChanged;

	public string CurrentLanguageTag { get; private set; } = "";

	public IReadOnlyList<string> SupportedLanguageTags => s_supportedLanguageTags;

	public LocalizationService(string languageTag) => ApplyLanguageTag(languageTag);

	public void ApplyLanguageTag(string languageTag)
	{
		var resolvedLanguageTag = ResolveSupportedLanguageTag(languageTag);
		var primaryLanguageOverride = string.IsNullOrWhiteSpace(languageTag) ? resolvedLanguageTag : languageTag;
		if (CurrentLanguageTag == resolvedLanguageTag && string.Equals(ApplicationLanguages.PrimaryLanguageOverride, primaryLanguageOverride, StringComparison.Ordinal)) return;

		ApplicationLanguages.PrimaryLanguageOverride = primaryLanguageOverride;
		ApplyCurrentThreadCultures(resolvedLanguageTag);

		_resourceLoader = new ResourceLoader();
		CurrentLanguageTag = resolvedLanguageTag;

		LanguageChanged?.Invoke();
	}

	public string GetFormattedString(string resourceName, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, GetLocalizedString(resourceName), arguments);

	public string GetLocalizedString(string resourceName)
	{
		var normalizedResourceName = resourceName.Replace('.', '/');
		string localizedString;
		try { localizedString = _resourceLoader.GetString(normalizedResourceName); }
		catch (COMException) { localizedString = resourceName; }

		return string.IsNullOrWhiteSpace(localizedString) ? resourceName : localizedString;
	}

	public string GetLanguageDisplayName(string languageTag) => languageTag switch
	{
		"" => GetLocalizedString("Menu.LanguageSystem"),
		"en-US" => "English",
		"ko-KR" => "한국어",
		"ja-JP" => "日本語",
		"zh-Hans" => "简体中文",
		"zh-Hant" => "繁體中文",
		_ => languageTag
	};

	private static void ApplyCurrentThreadCultures(string languageTag)
	{
		if (string.IsNullOrWhiteSpace(languageTag)) return;

		try
		{
			var cultureInfo = CultureInfo.GetCultureInfo(languageTag);
			CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
			CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
		}
		catch (CultureNotFoundException) { }
	}

	private static string ResolveSupportedLanguageTag(string languageTag)
	{
		var normalizedLanguageTag = NormalizeSupportedLanguageTag(languageTag);
		if (!string.IsNullOrWhiteSpace(normalizedLanguageTag)) return normalizedLanguageTag;

		normalizedLanguageTag = NormalizeSupportedLanguageTag(CultureInfo.InstalledUICulture.Name);
		return string.IsNullOrWhiteSpace(normalizedLanguageTag) ? s_supportedLanguageTags[0] : normalizedLanguageTag;
	}

	private static string NormalizeSupportedLanguageTag(string languageTag)
	{
		if (string.IsNullOrWhiteSpace(languageTag)) return "";
		if (s_supportedLanguageTags.Contains(languageTag)) return languageTag;

		try
		{
			var cultureInfo = CultureInfo.GetCultureInfo(languageTag);
			if (s_supportedLanguageTags.Contains(cultureInfo.Parent.Name)) return cultureInfo.Parent.Name;
		}
		catch (CultureNotFoundException) { }

		return "";
	}
}