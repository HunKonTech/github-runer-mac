using System.Text.Json;
using System.Text.Json.Nodes;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class PreferencesStore : IPreferencesStore
{
    private const string FileName = "settings.json";
    private readonly string _filePath;
    private JsonObject _settings = new();

    public PreferencesStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath();
        var folder = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(folder))
            folder = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(folder);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _settings = JsonNode.Parse(json)?.AsObject() ?? new();
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
        get => GetEnum(PreferenceDefaults.Language, "Language");
        set
        {
            _settings["Language"] = value.ToString();
            Save();
        }
    }

    public string RunnerDirectory
    {
        get => GetString(DefaultRunnerDirectory(), "RunnerDirectory");
        set
        {
            _settings["RunnerDirectory"] = value;
            Save();
        }
    }

    public RunnerControlMode ControlMode
    {
        get => GetEnum(PreferenceDefaults.ControlMode, "ControlMode");
        set
        {
            _settings["ControlMode"] = value.ToString();
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
        get => GetEnum(PreferenceDefaults.UpdateChannel, "UpdateChannel");
        set
        {
            _settings["UpdateChannel"] = value.ToString();
            Save();
        }
    }

    public bool StopRunnerOnBattery
    {
        get => GetBool(PreferenceDefaults.StopRunnerOnBattery, "StopRunnerOnBattery");
        set
        {
            _settings["StopRunnerOnBattery"] = value;
            Save();
        }
    }

    private TEnum GetEnum<TEnum>(TEnum defaultValue, string key) where TEnum : struct, Enum
    {
        var str = GetString("", key);
        if (!string.IsNullOrWhiteSpace(str))
        {
            if (Enum.TryParse<TEnum>(str, out var result))
                return result;
        }
        return defaultValue;
    }

    private bool GetBool(bool defaultValue, string key)
    {
        if (_settings.TryGetPropertyValue(key, out var value))
        {
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var b)) return b;
            if (bool.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return defaultValue;
    }

    private string GetString(string defaultValue, string key)
    {
        if (!_settings.TryGetPropertyValue(key, out var value))
            return defaultValue;

        var result = value?.ToString() ?? defaultValue;
        return string.IsNullOrWhiteSpace(result) ? defaultValue : result;
    }

    private static string DefaultRunnerDirectory()
    {
        if (OperatingSystem.IsMacOS())
            return PreferenceDefaults.MacOsRunnerDirectory;

        if (OperatingSystem.IsLinux())
            return "/home/" + Environment.UserName + "/actions-runner";

        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "GitHub", "actions-runner");
        }

        return "/actions-runner";
    }

    private static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "GitHubRunnerTray", FileName);
    }
}

public class PreferencesStoreFactory : IPreferencesStoreFactory
{
    public IPreferencesStore Create()
    {
        return new PreferencesStore();
    }
}
