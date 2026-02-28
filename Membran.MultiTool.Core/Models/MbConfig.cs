namespace Membran.MultiTool.Core.Models;

public sealed record MbConfig
{
    public bool DryRunDefault { get; init; }
    public bool AutoYesDefault { get; init; }
    public bool RestorePointDefault { get; init; } = true;
    public string OutputFormat { get; init; } = "text";
    public int OsintTimeoutSec { get; init; } = 120;
    public bool OsintRunAllFoundDefault { get; init; } = true;
    public int OsintMaxParallelTools { get; init; } = 3;
    public bool OsintOutputIncludeRaw { get; init; } = true;
}
