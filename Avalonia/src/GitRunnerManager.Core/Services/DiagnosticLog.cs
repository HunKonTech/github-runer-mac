namespace GitRunnerManager.Core.Services;

public static class DiagnosticLog
{
    private static readonly object SyncRoot = new();

    public static string DefaultLogDirectory => GetLogDirectory();
    public static string DefaultLogPath => Path.Combine(DefaultLogDirectory, "avalonia.log");
    public static string FallbackLogPath => Path.Combine(Path.GetTempPath(), "GitRunnerManager", "Logs", "avalonia.log");

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
        var entry = FormatEntry(message, exception);

        try
        {
            AppendEntry(path, entry);
        }
        catch (Exception writeException)
        {
            try
            {
                if (!string.Equals(path, FallbackLogPath, StringComparison.OrdinalIgnoreCase))
                    AppendEntry(FallbackLogPath, FormatEntry($"Primary diagnostic log failed: {writeException.Message}", writeException) + entry);
            }
            catch
            {
            }
        }
    }

    private static void AppendEntry(string path, string entry)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        lock (SyncRoot)
        {
            File.AppendAllText(path, entry);
        }
    }

    private static string FormatEntry(string message, Exception? exception)
    {
        var lines = new List<string>
        {
            $"[{DateTimeOffset.UtcNow:O}] {message}"
        };

        if (exception != null)
            lines.Add(exception.ToString());

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
