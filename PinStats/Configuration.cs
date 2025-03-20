using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;

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
        lock (LockObject)
        {
            ValidateConfigurationFile();
            var content = File.ReadAllText(ConfigurationFilePath).Trim();
            try { JsonNode.Parse(content); }
            catch (Exception) { content = File.ReadAllText(ConfigurationBackupFilePath).Trim(); }
            if (string.IsNullOrEmpty(content)) content = "{}";
            return content;
        }
    }

    public static Dictionary<string, object> GetConfigurationFileContent()
    {
        if (s_cache == null)
        {
            var configurationFileContentString = GetConfigurationFileContentString();
            var convertedFileContent = JsonSerializer.Deserialize(configurationFileContentString, SourceGenerationContext.Default.DictionaryStringObject);
            s_cache = new Dictionary<string, object>(convertedFileContent);
        }
        return s_cache;
    }

    public static T GetValue<T>(string key)
    {
        lock (LockObject)
        {
            var convertedFileContent = GetConfigurationFileContent();
            if (!convertedFileContent.TryGetValue(key, out object rawValue)) return default;
            if (rawValue is JsonElement element)
            {
                var value = element.Deserialize(jsonTypeInfo: SourceGenerationContext.Default.GetTypeInfo(typeof(T)));
                convertedFileContent[key] = value;
                return (T)value;
            }
            else if (rawValue is JsonArray array)
            {
                var value = array.Deserialize(jsonTypeInfo: SourceGenerationContext.Default.GetTypeInfo(typeof(T)));
                convertedFileContent[key] = value;
                return (T)value;
            }
            else if (rawValue is T value) return value;
            else return default;
        }
    }

    private static string s_buffer;
    private static System.Timers.Timer s_timer;

    public static void SetValue(string key, object value)
    {
        lock (LockObject)
        {
            var convertedFileContent = GetConfigurationFileContent();
            if (convertedFileContent.ContainsKey(key)) convertedFileContent[key] = value;
            else convertedFileContent.TryAdd(key, value);
            s_buffer = JsonSerializer.Serialize(convertedFileContent, SourceGenerationContext.Default.DictionaryStringObject);

            if (s_timer == null)
            {
                s_timer = new() { AutoReset = false };
                s_timer.Elapsed += (s, e) =>
                {
                    WriteBuffer();
                };
                s_timer.Interval = 50;
            }
            s_timer.Stop();
            s_timer.Start();
        }
    }

    public static void Import(string json)
    {
        lock (LockObject)
        {
            var content = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.DictionaryStringObject);
            var convertedFileContent = GetConfigurationFileContent();
            foreach (var (key, value) in content)
            {
                if (convertedFileContent.ContainsKey(key)) convertedFileContent[key] = value;
                else convertedFileContent.TryAdd(key, value);
            }
            s_cache = new(convertedFileContent);
            s_buffer = JsonSerializer.Serialize(convertedFileContent, SourceGenerationContext.Default.DictionaryStringObject);
            WriteBuffer();
        }
    }

    public static string Export()
    {
        lock (LockObject)
        {
            var convertedFileContent = GetConfigurationFileContent();
            return JsonSerializer.Serialize(convertedFileContent, SourceGenerationContext.Default.DictionaryStringObject);
        }
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
            lock (LockObject)
            {
                File.WriteAllText(ConfigurationFilePath, s_buffer);
                File.WriteAllText(ConfigurationBackupFilePath, s_buffer);
            }
        }
    }
}

[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(nuint))]
[JsonSerializable(typeof(nint))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(DateTimeOffset?))]
[JsonSerializable(typeof(DateTime?))]
[JsonSerializable(typeof(TimeSpan?))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(uint?))]
[JsonSerializable(typeof(ulong?))]
[JsonSerializable(typeof(byte?))]
[JsonSerializable(typeof(sbyte?))]
[JsonSerializable(typeof(char?))]
[JsonSerializable(typeof(decimal?))]
[JsonSerializable(typeof(float?))]
[JsonSerializable(typeof(nuint?))]
[JsonSerializable(typeof(nint?))]
[JsonSerializable(typeof(short?))]
[JsonSerializable(typeof(ushort?))]
[JsonSerializable(typeof(List<bool>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<DateTimeOffset>))]
[JsonSerializable(typeof(List<DateTime>))]
[JsonSerializable(typeof(List<TimeSpan>))]
[JsonSerializable(typeof(List<long>))]
[JsonSerializable(typeof(List<double>))]
[JsonSerializable(typeof(List<uint>))]
[JsonSerializable(typeof(List<ulong>))]
[JsonSerializable(typeof(List<byte>))]
[JsonSerializable(typeof(List<sbyte>))]
[JsonSerializable(typeof(List<char>))]
[JsonSerializable(typeof(List<decimal>))]
[JsonSerializable(typeof(List<float>))]
[JsonSerializable(typeof(List<nuint>))]
[JsonSerializable(typeof(List<nint>))]
[JsonSerializable(typeof(List<short>))]
[JsonSerializable(typeof(List<ushort>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonArray))]
internal partial class SourceGenerationContext : JsonSerializerContext;
