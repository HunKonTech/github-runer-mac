using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public static class GitHubActionsDiagnosticExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(GitHubActionsDiagnosticContext context)
    {
        return JsonSerializer.Serialize(context, JsonOptions);
    }

    public static string ToMarkdownPrompt(GitHubActionsDiagnosticContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Analyze this GitHub Actions run/job failure or active runner state. The local self-hosted runner appears to be associated with the following workflow run. Use the correlation confidence before assuming the local runner is the exact worker.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine($"- Exported at: {context.ExportedAt:O}");
        builder.AppendLine($"- GitHub account: {Value(context.Account.Login)}");
        builder.AppendLine($"- Repository: {Value(context.Run?.RepositoryFullName)}");
        builder.AppendLine($"- Workflow: {Value(context.Run?.WorkflowName)}");
        builder.AppendLine($"- Run ID: {context.Run?.Id.ToString() ?? "-"}");
        builder.AppendLine($"- Run number: {context.Run?.RunNumber.ToString() ?? "-"}");
        builder.AppendLine($"- Run URL: {Value(context.Run?.HtmlUrl)}");
        builder.AppendLine($"- Run status: {Value(context.Run?.Status)}");
        builder.AppendLine($"- Run conclusion: {Value(context.Run?.Conclusion)}");
        builder.AppendLine($"- Correlation confidence: {context.CorrelationConfidence}");
        builder.AppendLine($"- Correlation reason: {Value(context.CorrelationReason)}");
        builder.AppendLine();
        builder.AppendLine("## Local Runner");
        builder.AppendLine($"- Runner name: {Value(context.LocalRunner?.DisplayName)}");
        builder.AppendLine($"- Runner directory: {Value(context.LocalRunner?.RunnerDirectory)}");
        builder.AppendLine($"- Local status: {(context.LocalRunnerStatus.IsRunning ? "running" : "stopped")}");
        builder.AppendLine($"- Local activity: {Value(context.LocalRunnerStatus.Activity.Description)}");
        builder.AppendLine($"- Local activity job: {Value(context.LocalRunnerStatus.Activity.CurrentJobName)}");
        builder.AppendLine($"- Worker process active: {context.ResourceUsage.IsJobActive}");
        builder.AppendLine($"- CPU: {context.ResourceUsage.CpuPercent:0.0}%");
        builder.AppendLine($"- Memory: {context.ResourceUsage.MemoryMB:0.0} MB");
        builder.AppendLine();
        builder.AppendLine("## Jobs");

        foreach (var job in context.Jobs)
        {
            var marker = context.CurrentJob?.Id == job.Id ? " (current/correlated)" : "";
            builder.AppendLine($"- {job.Name}{marker}");
            builder.AppendLine($"  - ID: {job.Id}");
            builder.AppendLine($"  - Status: {Value(job.Status)}");
            builder.AppendLine($"  - Conclusion: {Value(job.Conclusion)}");
            builder.AppendLine($"  - Runner name: {Value(job.RunnerName)}");
            builder.AppendLine($"  - Runner group: {Value(job.RunnerGroupName)}");
            builder.AppendLine($"  - Labels: {(job.Labels.Count == 0 ? "-" : string.Join(", ", job.Labels))}");
            builder.AppendLine($"  - Started: {job.StartedAt?.ToString("O") ?? "-"}");
            builder.AppendLine($"  - Completed: {job.CompletedAt?.ToString("O") ?? "-"}");
            builder.AppendLine($"  - Correlation confidence: {job.CorrelationConfidence}");
            if (!string.IsNullOrWhiteSpace(job.CorrelationReason))
                builder.AppendLine($"  - Correlation reason: {job.CorrelationReason}");
            if (job.Steps.Count > 0)
            {
                builder.AppendLine("  - Steps:");
                foreach (var step in job.Steps)
                    builder.AppendLine($"    - {step.Number}. {step.Name}: {Value(step.Status)} / {Value(step.Conclusion)}");
            }
        }

        if (context.LastRelevantRunnerLogLines.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Last Relevant Runner Log Lines");
            builder.AppendLine("```text");
            foreach (var line in context.LastRelevantRunnerLogLines)
                builder.AppendLine(line);
            builder.AppendLine("```");
        }

        if (!string.IsNullOrWhiteSpace(context.PermissionStatus.Message) || !string.IsNullOrWhiteSpace(context.PermissionStatus.TechnicalDetails))
        {
            builder.AppendLine();
            builder.AppendLine("## GitHub Permissions");
            builder.AppendLine($"- Workflow access: {context.PermissionStatus.HasWorkflowAccess}");
            builder.AppendLine($"- Repository runner access: {context.PermissionStatus.HasRepositoryRunnerAccess}");
            builder.AppendLine($"- Organization runner access: {context.PermissionStatus.HasOrganizationRunnerAccess}");
            builder.AppendLine($"- Runner admin access: {context.PermissionStatus.HasRunnerAdminAccess}");
            builder.AppendLine($"- Message: {Value(context.PermissionStatus.Message)}");
            builder.AppendLine($"- Technical details: {Value(context.PermissionStatus.TechnicalDetails)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyList<string> ReadRelevantRunnerLogLines(string runnerDirectory, int maxLines = 30)
    {
        var diagPath = Path.Combine(runnerDirectory, "_diag");
        if (!Directory.Exists(diagPath))
            return [];

        var latestLog = new DirectoryInfo(diagPath)
            .GetFiles("Runner_*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latestLog == null)
            return [];

        var markers = new[] { "Running job:", "completed with result:", "Listening for Jobs", "Runner.Worker", "Job", "error", "fail" };
        return File.ReadLines(latestLog.FullName)
            .Where(line => markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .TakeLast(maxLines)
            .ToList();
    }

    private static string Value(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }
}
