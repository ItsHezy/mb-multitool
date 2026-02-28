using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Osint.Services;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _definitions;

    public ToolRegistry()
    {
        _definitions = BuildDefinitions()
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> ListDefinitions()
    {
        return _definitions.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListToolNames()
    {
        return _definitions.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryGetDefinition(string toolName, out ToolDefinition definition)
    {
        return _definitions.TryGetValue(toolName, out definition!);
    }

    public bool SupportsTarget(string toolName, OsintTargetType targetType)
    {
        return _definitions.TryGetValue(toolName, out var definition)
               && definition.Capabilities.Contains(targetType);
    }

    private static IReadOnlyList<ToolDefinition> BuildDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = "maltego",
                DisplayName = "Maltego",
                Capabilities = [OsintTargetType.Phone, OsintTargetType.Domain, OsintTargetType.Ip, OsintTargetType.Email, OsintTargetType.Username],
                CommandCandidates = ["maltego", "maltego.exe"],
                KnownWindowsPaths =
                [
                    @"C:\Program Files\Maltego\maltego.exe",
                    @"C:\Program Files (x86)\Maltego\maltego.exe"
                ],
                ManualOnly = true,
                WingetId = "Maltego.Maltego",
                InstallHint = "Install Maltego Community Edition from https://www.maltego.com/downloads/.",
                Notes = ["Desktop tool, scripted execution is manual-step only in v1."]
            },
            new ToolDefinition
            {
                Name = "sherlock",
                DisplayName = "Sherlock",
                Capabilities = [OsintTargetType.Username],
                CommandCandidates = ["sherlock", "sherlock.exe"],
                PipPackage = "sherlock-project",
                InstallHint = "pip install sherlock-project"
            },
            new ToolDefinition
            {
                Name = "recon-ng",
                DisplayName = "Recon-ng",
                Capabilities = [OsintTargetType.Domain, OsintTargetType.Ip, OsintTargetType.Email, OsintTargetType.Phone],
                CommandCandidates = ["recon-ng", "recon-ng.exe"],
                InstallHint = "Install recon-ng from https://github.com/lanmaster53/recon-ng (pip package is not available).",
                Notes = ["Complex workspace automation is reduced in v1; wrapper runs quick module-compatible probes only."]
            },
            new ToolDefinition
            {
                Name = "spiderfoot",
                DisplayName = "SpiderFoot",
                Capabilities = [OsintTargetType.Domain, OsintTargetType.Ip, OsintTargetType.Email, OsintTargetType.Phone],
                CommandCandidates = ["spiderfoot", "spiderfoot.exe", "sf.py"],
                KnownWindowsPaths =
                [
                    @"C:\Program Files\SpiderFoot\spiderfoot.exe"
                ],
                InstallHint = "Install SpiderFoot from https://github.com/smicallef/spiderfoot/releases (pip package is not available)."
            },
            new ToolDefinition
            {
                Name = "phoneinfoga",
                DisplayName = "PhoneInfoga",
                Capabilities = [OsintTargetType.Phone],
                CommandCandidates = ["phoneinfoga", "phoneinfoga.exe"],
                InstallHint = "Download PhoneInfoga binary from https://github.com/sundowndev/phoneinfoga/releases."
            },
            new ToolDefinition
            {
                Name = "holehe",
                DisplayName = "Holehe",
                Capabilities = [OsintTargetType.Email],
                CommandCandidates = ["holehe", "holehe.exe"],
                PipPackage = "holehe",
                InstallHint = "pip install holehe"
            },
            new ToolDefinition
            {
                Name = "maigret",
                DisplayName = "Maigret",
                Capabilities = [OsintTargetType.Username],
                CommandCandidates = ["maigret", "maigret.exe"],
                PipPackage = "maigret",
                InstallHint = "pip install maigret"
            },
            new ToolDefinition
            {
                Name = "theharvester",
                DisplayName = "theHarvester",
                Capabilities = [OsintTargetType.Domain, OsintTargetType.Email],
                CommandCandidates = ["theHarvester", "theHarvester.exe", "theharvester", "theharvester.exe"],
                PipPackage = "git+https://github.com/laramies/theHarvester.git",
                InstallHint = "Install from source: pip install git+https://github.com/laramies/theHarvester.git"
            },
            new ToolDefinition
            {
                Name = "bbot",
                DisplayName = "BBOT",
                Capabilities = [OsintTargetType.Domain, OsintTargetType.Ip],
                CommandCandidates = ["bbot", "bbot.exe"],
                PipPackage = "bbot",
                InstallHint = "pip install bbot"
            },
            new ToolDefinition
            {
                Name = "osrframework",
                DisplayName = "OSRFramework",
                Capabilities = [OsintTargetType.Username, OsintTargetType.Email, OsintTargetType.Domain],
                CommandCandidates = ["usufy", "mailfy", "domainfy", "searchfy"],
                PipPackage = "osrframework",
                InstallHint = "pip install osrframework",
                Notes = ["OSRFramework uses target-specific commands (usufy/mailfy/domainfy)."]
            }
        ];
    }
}

public sealed class ToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public OsintTargetType[] Capabilities { get; init; } = Array.Empty<OsintTargetType>();
    public string[] CommandCandidates { get; init; } = Array.Empty<string>();
    public string[] KnownWindowsPaths { get; init; } = Array.Empty<string>();
    public bool ManualOnly { get; init; }
    public string? PipPackage { get; init; }
    public string? WingetId { get; init; }
    public string InstallHint { get; init; } = string.Empty;
    public string[] Notes { get; init; } = Array.Empty<string>();
}
