namespace GitRunnerManager.Core.Services;

public static class DiagnosticLog
{
    public static string DefaultLogDirectory => GetLogDirectory();
    public static string DefaultLogPath => Path.Combine(DefaultLogDirectory, "avalonia.log");

    public static void Write(string message, string? logPath = null)
    {
        WriteEntry(message, null, logPath);
    }

    public static void WriteException(string message, Exception exception, string? logPath = null)
    {
        WriteEntry(message, exception, logPath);
    }

    private static void WriteEntry(string message, Exception? exception, string? logPath)
    {
        var path = logPath ?? DefaultLogPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>
        {
            $"[{DateTimeOffset.UtcNow:O}] {message}"
        };

        if (exception != null)
            lines.Add(exception.ToString());

        File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static string GetLogDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return Path.Combine(home, "Library", "Logs", "GitRunnerManager");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            return Path.Combine(localAppData, "GitRunnerManager", "Logs");

        return Path.Combine(Path.GetTempPath(), "GitRunnerManager", "Logs");
    }
}
