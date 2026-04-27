using System.Text.Json;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class PreferencesStore : IPreferencesStore
{
    private const string FileName = "settings.json";
    private readonly string _filePath;
    private Dictionary<string, object> _settings = new();

    public PreferencesStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "GitHubRunnerTray");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, FileName);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
        }
        catch
        {
            _settings = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    public AppLanguage Language
    {
        get => GetEnum(AppLanguage.System, "Language");
        set
        {
            _settings["Language"] = value.ToString();
            Save();
        }
    }

    public string RunnerDirectory
    {
        get => GetString("", "RunnerDirectory");
        set
        {
            _settings["RunnerDirectory"] = value;
            Save();
        }
    }

    public bool AutomaticUpdateCheckEnabled
    {
        get => GetBool(false, "AutomaticUpdateCheckEnabled");
        set
        {
            _settings["AutomaticUpdateCheckEnabled"] = value;
            Save();
        }
    }

    public UpdateChannel UpdateChannel
    {
        get => GetUpdateChannel(UpdateChannel.Stable, "UpdateChannel");
        set
        {
            _settings["UpdateChannel"] = value.ToString();
            Save();
        }
    }

    public bool StopRunnerOnBattery
    {
        get => GetBool(false, "StopRunnerOnBattery");
        set
        {
            _settings["StopRunnerOnBattery"] = value;
            Save();
        }
    }

    private AppLanguage GetEnum(AppLanguage defaultValue, string key)
    {
        if (_settings.TryGetValue(key, out var value) && value is string str)
        {
            if (Enum.TryParse<AppLanguage>(str, out var result))
                return result;
        }
        return defaultValue;
    }

    private UpdateChannel GetUpdateChannel(UpdateChannel defaultValue, string key)
    {
        if (_settings.TryGetValue(key, out var value) && value is string str)
        {
            if (Enum.TryParse<UpdateChannel>(str, out var result))
                return result;
        }
        return defaultValue;
    }

    private bool GetBool(bool defaultValue, string key)
    {
        if (_settings.TryGetValue(key, out var value))
        {
            if (value is bool b) return b;
            if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
        }
        return defaultValue;
    }

    private string GetString(string defaultValue, string key)
    {
        return _settings.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;
    }
}

public class PreferencesStoreFactory : IPreferencesStoreFactory
{
    public IPreferencesStore Create()
    {
        return new PreferencesStore();
    }
}