using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public static class GitHubPermissionEvaluator
{
    public static GitHubPermissionEvaluation Evaluate(bool isSignedIn, IEnumerable<string> scopes)
    {
        var normalized = scopes
            .Select(scope => scope.Trim())
            .Where(scope => scope.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasRepo = normalized.Contains("repo", StringComparer.OrdinalIgnoreCase);
        var hasAdminOrg = normalized.Contains("admin:org", StringComparer.OrdinalIgnoreCase);
        var hasUserOrReadOrg = normalized.Contains("user", StringComparer.OrdinalIgnoreCase)
            || normalized.Contains("read:org", StringComparer.OrdinalIgnoreCase)
            || hasAdminOrg;

        return new GitHubPermissionEvaluation
        {
            IsSignedIn = isSignedIn,
            Scopes = normalized,
            HasRepoScope = hasRepo,
            HasAdminOrgScope = hasAdminOrg,
            HasUserOrReadOrgScope = hasUserOrReadOrg,
            MissingRepositoryRunnerScopes = hasRepo ? [] : ["repo"],
            MissingOrganizationRunnerScopes = hasRepo && hasAdminOrg ? [] : new[] { hasRepo ? "" : "repo", hasAdminOrg ? "" : "admin:org" }.Where(value => value.Length > 0).ToList(),
            Message = isSignedIn
                ? normalized.Count == 0 ? "Signed in, but GitHub did not report OAuth scopes for this token." : $"OAuth scopes: {string.Join(", ", normalized)}"
                : "Not signed in."
        };
    }
}
