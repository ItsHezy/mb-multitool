namespace Membran.MultiTool.Core.Models;

public enum OsintTargetType
{
    Phone,
    Username,
    Email,
    Domain,
    Ip
}

public sealed class OsintScanRequest
{
    public string ScanId { get; init; } = Guid.NewGuid().ToString("N");
    public OsintTargetType TargetType { get; init; }
    public string TargetValue { get; init; } = string.Empty;
    public bool ConsentAcknowledged { get; init; }
    public string[] RequestedTools { get; init; } = Array.Empty<string>();
    public int TimeoutSec { get; init; } = 120;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ToolAvailability
{
    public string ToolName { get; init; } = string.Empty;
    public bool Detected { get; init; }
    public string? Version { get; init; }
    public string? Path { get; init; }
    public OsintTargetType[] CapabilityTargets { get; init; } = Array.Empty<OsintTargetType>();
    public string[] Notes { get; init; } = Array.Empty<string>();
}

public sealed class OsintFinding
{
    public string Source { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Indicator { get; init; } = string.Empty;
    public string Confidence { get; init; } = "low";
    public string Context { get; init; } = string.Empty;
}

public sealed class ToolExecutionResult
{
    public string ToolName { get; init; } = string.Empty;
    public bool Executed { get; init; }
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public int ExitCode { get; init; }
    public long DurationMs { get; init; }
    public string ParsedSummary { get; init; } = string.Empty;
    public string RawStdOut { get; init; } = string.Empty;
    public string RawStdErr { get; init; } = string.Empty;
    public OsintFinding[] Findings { get; init; } = Array.Empty<OsintFinding>();
}

public sealed class PhoneParseResult
{
    public string Input { get; init; } = string.Empty;
    public string E164 { get; init; } = string.Empty;
    public bool IsPossible { get; init; }
    public bool IsValid { get; init; }
    public string RegionCode { get; init; } = string.Empty;
    public string NumberType { get; init; } = string.Empty;
    public string Carrier { get; init; } = string.Empty;
    public string[] TimeZones { get; init; } = Array.Empty<string>();
    public string InternationalFormat { get; init; } = string.Empty;
    public string NationalFormat { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class OsintScanReport
{
    public string ScanId { get; init; } = Guid.NewGuid().ToString("N");
    public OsintTargetType TargetType { get; init; }
    public string TargetValue { get; init; } = string.Empty;
    public DateTimeOffset ConsentAcknowledgedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool RemovedSensitiveInText { get; init; }
    public PhoneParseResult? PhoneParse { get; init; }
    public ToolExecutionResult[] ToolResults { get; init; } = Array.Empty<ToolExecutionResult>();
    public OsintFinding[] AggregateFindings { get; init; } = Array.Empty<OsintFinding>();
    public string[] Warnings { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
