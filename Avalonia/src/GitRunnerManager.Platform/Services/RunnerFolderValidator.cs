using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class RunnerFolderValidator : IRunnerFolderValidator
{
    private static readonly string[] RequiredAnyMarkers =
    [
        "run.sh",
        "run.cmd",
        "run.bat",
        "config.sh",
        "config.cmd",
        ".runner",
        "_diag"
    ];

    public RunnerFolderValidationResult Validate(string runnerDirectory)
    {
        if (string.IsNullOrWhiteSpace(runnerDirectory))
            return new RunnerFolderValidationResult { IsValid = false, Message = "Runner directory is required." };

        if (!Directory.Exists(runnerDirectory))
            return new RunnerFolderValidationResult { IsValid = false, Message = "The selected folder does not exist." };

        var markers = RequiredAnyMarkers
            .Where(marker =>
            {
                var path = Path.Combine(runnerDirectory, marker);
                return File.Exists(path) || Directory.Exists(path);
            })
            .ToList();

        var hasRunScript = markers.Any(marker => marker is "run.sh" or "run.cmd" or "run.bat");
        var hasConfigMarker = markers.Any(marker => marker is "config.sh" or "config.cmd" or ".runner" or "_diag");
        var isValid = hasRunScript && hasConfigMarker;

        return new RunnerFolderValidationResult
        {
            IsValid = isValid,
            Message = isValid
                ? "Runner folder validated."
                : "The selected folder does not look like a GitHub Actions runner directory.",
            Markers = markers
        };
    }

    public RunnerFolderSetupValidationResult ValidateSetupFolder(string runnerDirectory, RunnerFolderSetupMode mode)
    {
        if (string.IsNullOrWhiteSpace(runnerDirectory))
            return new RunnerFolderSetupValidationResult { IsValid = false, Message = "Runner directory is required." };

        if (mode == RunnerFolderSetupMode.ImportExisting)
        {
            var import = Validate(runnerDirectory);
            return new RunnerFolderSetupValidationResult { IsValid = import.IsValid, Message = import.Message };
        }

        try
        {
            if (File.Exists(runnerDirectory))
                return new RunnerFolderSetupValidationResult { IsValid = false, Message = "The selected path is a file." };

            if (!Directory.Exists(runnerDirectory))
            {
                var parent = Directory.GetParent(Path.GetFullPath(runnerDirectory));
                if (parent == null || !parent.Exists)
                    return new RunnerFolderSetupValidationResult { IsValid = false, Message = "The parent folder does not exist." };

                return CanWrite(parent.FullName);
            }

            var entries = Directory.EnumerateFileSystemEntries(runnerDirectory).Take(20).ToList();
            if (entries.Any(entry => Path.GetFileName(entry) is ".runner" or ".credentials" or ".credentials_rsaparams"))
                return new RunnerFolderSetupValidationResult { IsValid = false, Message = "The selected folder already contains a configured runner." };

            if (entries.Count > 0 && !entries.Any(entry => Path.GetFileName(entry).StartsWith("actions-runner-", StringComparison.OrdinalIgnoreCase)))
                return new RunnerFolderSetupValidationResult { IsValid = false, Message = "Choose an empty folder or an existing runner package folder." };

            return CanWrite(runnerDirectory);
        }
        catch (Exception ex)
        {
            return new RunnerFolderSetupValidationResult { IsValid = false, Message = ex.Message };
        }
    }

    private static RunnerFolderSetupValidationResult CanWrite(string folder)
    {
        var probe = Path.Combine(folder, $".grm-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return new RunnerFolderSetupValidationResult { IsValid = true, Message = "Runner folder is ready." };
        }
        catch
        {
            return new RunnerFolderSetupValidationResult { IsValid = false, Message = "The selected folder is not writable." };
        }
    }
}
