using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PinStats;

public class Configuration
{
	private readonly static object LockObject = new();
	private readonly static string BasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private const string ConfigurationDirectoryName = "PinStats";

    private const string ConfigurationFileName = "settings.json";
	private const string ConfigurationBackupFileName = "settings.json.bak";

	private readonly static string ConfigurationDirectoryPath = Path.Combine(BasePath, ConfigurationDirectoryName);
	private readonly static string ConfigurationFilePath = Path.Combine(ConfigurationDirectoryPath, ConfigurationFileName);
	private readonly static string ConfigurationBackupFilePath = Path.Combine(ConfigurationDirectoryPath, ConfigurationBackupFileName);

	private static Dictionary<string, object> s_cache;

	private static void ValidateConfigurationFile()
	{
		if (!Directory.Exists(ConfigurationDirectoryPath))
			Directory.CreateDirectory(ConfigurationDirectoryPath);
		if (!File.Exists(ConfigurationFilePath))
			File.Create(ConfigurationFilePath).Close();
		if (!File.Exists(ConfigurationBackupFilePath))
			File.Create(ConfigurationBackupFilePath).Close();
	}

	private static string GetConfigurationFileContentString()
	{
		Monitor.Enter(LockObject);
		try
		{
			ValidateConfigurationFile();
			var content = File.ReadAllText(ConfigurationFilePath).Trim();
			try { JObject.Parse(content); }
			catch (Exception) { content = File.ReadAllText(ConfigurationBackupFilePath).Trim(); }
			if (string.IsNullOrEmpty(content)) content = "{}";
			return content;
		}
		finally { Monitor.Exit(LockObject); }
	}

	public static Dictionary<string, object> GetConfigurationFileContent()
	{
		if (s_cache == null)
		{
			var configurationFileContentString = GetConfigurationFileContentString();
			var convertedFileContent = JsonConvert.DeserializeObject<Dictionary<string, object>>(configurationFileContentString);
			s_cache = new Dictionary<string, object>(convertedFileContent);
		}
		return s_cache;
	}

	public static object GetValue(string key)
	{
		Monitor.Enter(LockObject);
		try
		{
			var convertedFileContent = GetConfigurationFileContent();
			if (convertedFileContent.TryGetValue(key, out object value)) return value;
			return null;
		}
		finally { Monitor.Exit(LockObject); }
	}

	public static T GetValue<T>(string key)
	{
		Monitor.Enter(LockObject);
		try
		{
			var convertedFileContent = GetConfigurationFileContent();
			if (!convertedFileContent.ContainsKey(key)) return default;
			var rawValue = convertedFileContent[key];
			if (rawValue is JToken content) return content.ToObject<T>();
			else if (rawValue is T value) return value;
			else return default;
		}
		finally { Monitor.Exit(LockObject); }
	}

	private static string s_buffer;
	private static System.Timers.Timer timer;

	public static void SetValue(string key, object value)
	{
		Monitor.Enter(LockObject);
		try
		{
			var convertedFileContent = GetConfigurationFileContent();
			if (convertedFileContent.ContainsKey(key)) convertedFileContent[key] = value;
			else convertedFileContent.TryAdd(key, value);
			s_buffer = JsonConvert.SerializeObject(convertedFileContent);

			if (timer == null)
			{
				timer = new() { AutoReset = false };
				timer.Elapsed += (s, e) =>
				{
					WriteBuffer();
				};
				timer.Interval = 50;
			}
			timer.Stop();
			timer.Start();
		}
		finally { Monitor.Exit(LockObject); }
	}

	public static bool IsExiting { get; set; }
	private static bool s_exited = false;
	public static void WriteBuffer()
	{
		if (IsExiting && !s_exited)
		{
			File.WriteAllText(ConfigurationFilePath, s_buffer);
			s_exited = true;
		}
		else if (!IsExiting)
		{
			Monitor.Enter(LockObject);
			try
			{
				File.WriteAllText(ConfigurationFilePath, s_buffer);
				File.WriteAllText(ConfigurationBackupFilePath, s_buffer);
			}
			finally { Monitor.Exit(LockObject); }
		}
	}
}
