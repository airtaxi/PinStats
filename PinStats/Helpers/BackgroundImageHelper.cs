using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PinStats.Enums;

namespace PinStats.Helpers;

public static class BackgroundImageHelper
{
	public static bool TrySetupBackgroundImage(BackgroundImageType backgroundImageType, Image image)
	{
		// Retrieve background image path and validate if available
		var backgroundImagePath = Configuration.GetValue<string>(backgroundImageType.ToString() + "BackgroundImagePath");
		if (backgroundImagePath == null) // Uninitialized or user manually reset
		{
			image.Source = null; // Reset image source
			return false;
		}
		else if (!File.Exists(backgroundImagePath)) return false; // File might be deleted

		// Initialize BitmapImage in dispatcher thread context	
		image.DispatcherQueue.TryEnqueue(async () =>
		{
			var bitmapImage = new BitmapImage();
			using var memoryStream = new MemoryStream();
			var bytes = await File.ReadAllBytesAsync(backgroundImagePath);
			await memoryStream.WriteAsync(bytes, 0, bytes.Length);
			memoryStream.Seek(0, SeekOrigin.Begin);
			await bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream());

			// Apply BitmapImage to background image source
			image.Source = bitmapImage;
		});

		return true;
	}
}
