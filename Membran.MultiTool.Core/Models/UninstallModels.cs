namespace Membran.MultiTool.Core.Models;

public sealed class DiscoveredApp
{
    public string AppId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string? Version { get; init; }
    public string? UninstallString { get; init; }
    public string? InstallLocation { get; init; }
    public bool IsSystemComponent { get; init; }
    public string RegistryPath { get; init; } = string.Empty;
}

public sealed class ManualRule
{
    public string NamePattern { get; init; } = string.Empty;
    public string[] FilePaths { get; init; } = Array.Empty<string>();
    public string[] RegistryPaths { get; init; } = Array.Empty<string>();
    public string[] ProcessNames { get; init; } = Array.Empty<string>();

    public static ManualRule Empty { get; } = new();
}

public sealed class UninstallArtifact
{
    public string ArtifactId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string PathOrKey { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "medium";
}

public sealed class UninstallPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N");
    public string AppId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? UninstallString { get; init; }
    public UninstallArtifact[] Artifacts { get; init; } = Array.Empty<UninstallArtifact>();
    public bool RequiresAdmin { get; init; }
    public bool RestorePointPlanned { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ArtifactExecutionResult
{
    public string ArtifactId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool Removed { get; init; }
    public bool Skipped { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ExecutionReport
{
    public string PlanId { get; init; } = string.Empty;
    public int RemovedCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public string[] Failures { get; init; } = Array.Empty<string>();
    public string[] Skipped { get; init; } = Array.Empty<string>();
    public ArtifactExecutionResult[] ArtifactResults { get; init; } = Array.Empty<ArtifactExecutionResult>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}

public sealed class UninstallExecutionOptions
{
    public bool Confirm { get; init; }
    public bool DryRun { get; init; }
    public bool AttemptRestorePoint { get; init; } = true;
    public bool AllowPartialWithoutAdmin { get; init; }
}
