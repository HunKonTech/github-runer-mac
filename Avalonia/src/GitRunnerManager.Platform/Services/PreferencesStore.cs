using System.Text.Json;
using System.Text.Json.Nodes;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class PreferencesStore : IPreferencesStore
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
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

    public string GitHubOAuthClientId
    {
        get => GetString("", "GitHubOAuthClientId");
        set
        {
            _settings["GitHubOAuthClientId"] = value;
            Save();
        }
    }

    public string RunnerDirectory
    {
        get => RunnerProfiles.FirstOrDefault()?.RunnerDirectory ?? GetString(DefaultRunnerDirectory(), "RunnerDirectory");
        set
        {
            _settings["RunnerDirectory"] = value;
            var profiles = RunnerProfiles;
            if (profiles.Count == 0)
                profiles.Add(CreateMigratedProfile(value));
            else
                profiles[0].RunnerDirectory = value;
            SetRunnerProfiles(profiles, save: false);
            Save();
        }
    }

    public List<RunnerConfig> RunnerProfiles
    {
        get
        {
            var profiles = GetStoredRunnerProfiles();
            if (profiles.Count > 0)
                return profiles;

            var legacyDirectory = GetString(DefaultRunnerDirectory(), "RunnerDirectory");
            if (string.IsNullOrWhiteSpace(legacyDirectory))
                return [];

            profiles = [CreateMigratedProfile(legacyDirectory)];
            SetRunnerProfiles(profiles, save: true);
            return profiles;
        }
        set => SetRunnerProfiles(value, save: true);
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

    public bool StopRunnerOnMeteredNetwork
    {
        get => GetBool(PreferenceDefaults.StopRunnerOnMeteredNetwork, "StopRunnerOnMeteredNetwork");
        set
        {
            _settings["StopRunnerOnMeteredNetwork"] = value;
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

    private List<RunnerConfig> GetStoredRunnerProfiles()
    {
        if (!_settings.TryGetPropertyValue("RunnerProfiles", out var value) || value == null)
            return [];

        try
        {
            return value.Deserialize<List<RunnerConfig>>(JsonOptions)?
                .Where(profile => !string.IsNullOrWhiteSpace(profile.RunnerDirectory))
                .Select(NormalizeProfile)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SetRunnerProfiles(List<RunnerConfig> profiles, bool save)
    {
        var normalized = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.RunnerDirectory))
            .Select(NormalizeProfile)
            .ToList();

        _settings["RunnerProfiles"] = JsonSerializer.SerializeToNode(normalized, JsonOptions);
        if (normalized.Count > 0)
            _settings["RunnerDirectory"] = normalized[0].RunnerDirectory;

        if (save)
            Save();
    }

    private RunnerConfig CreateMigratedProfile(string runnerDirectory)
    {
        var mode = ControlMode;
        return NormalizeProfile(new RunnerConfig
        {
            DisplayName = "Local runner",
            RunnerDirectory = runnerDirectory,
            AutoStartEnabled = mode != RunnerControlMode.ForceStopped,
            AutomaticModeEnabled = mode == RunnerControlMode.Automatic,
            StopOnBattery = StopRunnerOnBattery,
            StopOnMeteredNetwork = StopRunnerOnMeteredNetwork,
            IsEnabled = true
        });
    }

    private static RunnerConfig NormalizeProfile(RunnerConfig profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = Path.GetFileName(profile.RunnerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = "Local runner";

        profile.Labels ??= [];
        return profile;
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
        return Path.Combine(appData, "GitRunnerManager", FileName);
    }
}

public class PreferencesStoreFactory : IPreferencesStoreFactory
{
    public IPreferencesStore Create()
    {
        return new PreferencesStore();
    }
}
