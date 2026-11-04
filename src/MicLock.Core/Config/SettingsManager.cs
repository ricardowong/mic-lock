using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicLock.Core.Config;

public class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsFilePath;
    private readonly object _lock = new();

    public AppSettings Current { get; private set; }

    public SettingsManager(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsFilePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "MicLock");
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }

        Current = Load();
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.ClampValues();
                        Current = loaded;
                        return Current;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to fresh default settings on corrupted file
            }

            Current = new AppSettings();
            return Current;
        }
    }

    public void Save(AppSettings? settings = null)
    {
        lock (_lock)
        {
            if (settings != null)
            {
                Current = settings;
            }

            Current.ClampValues();

            string dir = Path.GetDirectoryName(_settingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempFile = _settingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(tempFile, json);

            // Atomic file replace to prevent corrupted writes on sudden power loss/shutdown
            if (File.Exists(_settingsFilePath))
            {
                File.Replace(tempFile, _settingsFilePath, null);
            }
            else
            {
                File.Move(tempFile, _settingsFilePath);
            }
        }
    }

    public string SettingsFilePath => _settingsFilePath;

    public void ExportSettings(string destinationPath)
    {
        lock (_lock)
        {
            Current.ClampValues();
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(destinationPath, json);
        }
    }

    public bool ImportSettings(string sourcePath)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(sourcePath))
                {
                    string json = File.ReadAllText(sourcePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.ClampValues();
                        Save(loaded);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
