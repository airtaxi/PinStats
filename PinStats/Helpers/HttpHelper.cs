namespace PinStats.Helpers;

public static class HttpHelper
{
	private static readonly HttpClient SharedHttpClient = new();
	public static async Task<string> GetContentFromUrlAsync(string url)
	{
		try
		{
			HttpResponseMessage response = await SharedHttpClient.GetAsync(url);
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsStringAsync();
		}
		catch (Exception) { return null; }
	}
}
