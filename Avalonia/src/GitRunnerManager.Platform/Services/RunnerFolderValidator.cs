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
}
