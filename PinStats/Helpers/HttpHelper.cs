namespace PinStats.Helpers;

public static class HttpHelper
{
	public static async Task<string> GetContentFromUrlAsync(string url)
	{
		using HttpClient httpClient = new HttpClient();
		try
		{
			HttpResponseMessage response = await httpClient.GetAsync(url);
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsStringAsync();
		}
		catch (HttpRequestException) { return null; }
	}
}
