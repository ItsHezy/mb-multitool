using System.Text.RegularExpressions;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Windows;

namespace Membran.MultiTool.Osint.Services;

public interface IToolDetector
{
    Task<IReadOnlyList<ToolAvailability>> DetectAllAsync(CancellationToken cancellationToken = default);
}

public sealed class ToolDetector(SystemCommandRunner commandRunner, ToolRegistry toolRegistry) : IToolDetector
{
    private readonly SystemCommandRunner _commandRunner = commandRunner;
    private readonly ToolRegistry _toolRegistry = toolRegistry;

    public async Task<IReadOnlyList<ToolAvailability>> DetectAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ToolAvailability>();
        foreach (var definition in _toolRegistry.ListDefinitions())
        {
            var (detected, path, commandName) = ResolveExecutable(definition);
            var notes = new List<string>(definition.Notes);
            string? version = null;

            if (!detected)
            {
                var packageDetection = await TryDetectByPackageManagerAsync(definition, cancellationToken).ConfigureAwait(false);
                detected = packageDetection.Detected;
                path = packageDetection.Path;
                commandName = packageDetection.CommandName;
                version = packageDetection.Version;
            }

            if (!detected)
            {
                notes.Add(definition.InstallHint);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(commandName))
                {
                    version ??= await ProbeVersionAsync(definition.Name, path, commandName, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    version ??= "detected";
                }

                if (definition.ManualOnly)
                {
                    notes.Add("Detected, but this tool requires manual desktop workflow for full analysis.");
                }
            }

            results.Add(new ToolAvailability
            {
                ToolName = definition.Name,
                Detected = detected,
                Path = path,
                Version = version,
                CapabilityTargets = definition.Capabilities,
                Notes = notes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        return results;
    }

    private async Task<string?> ProbeVersionAsync(
        string toolName,
        string executablePath,
        string commandName,
        CancellationToken cancellationToken)
    {
        var versionArgs = toolName switch
        {
            "phoneinfoga" => "version",
            _ => "--version"
        };

        var result = await _commandRunner
            .RunAsync(executablePath, versionArgs, timeoutMs: 8_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            if (toolName == "maltego")
            {
                return "detected";
            }

            if (commandName.Equals("spiderfoot", StringComparison.OrdinalIgnoreCase) ||
                commandName.Equals("sf.py", StringComparison.OrdinalIgnoreCase))
            {
                var altResult = await _commandRunner
                    .RunAsync(executablePath, "-h", timeoutMs: 8_000, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (altResult.ExitCode != 0)
                {
                    return null;
                }

                return "detected";
            }

            return null;
        }

        var text = string.Join(Environment.NewLine, result.StdOut, result.StdErr).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "detected";
        }

        var firstLine = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "detected";
        var compact = Regex.Replace(firstLine, @"\s+", " ").Trim();
        return compact.Length > 120 ? compact[..120] : compact;
    }

    private async Task<(bool Detected, string? Path, string? CommandName, string? Version)> TryDetectByPackageManagerAsync(
        ToolDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.Name.Equals("maltego", StringComparison.OrdinalIgnoreCase))
        {
            var wingetResult = await _commandRunner
                .RunAsync("winget", "list --id Maltego.Maltego -e", timeoutMs: 15_000, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (wingetResult.ExitCode == 0)
            {
                var output = string.Join(Environment.NewLine, wingetResult.StdOut, wingetResult.StdErr);
                if (output.Contains("Maltego.Maltego", StringComparison.OrdinalIgnoreCase))
                {
                    var versionMatch = Regex.Match(output, @"Maltego\.Maltego\s+([^\s]+)", RegexOptions.IgnoreCase);
                    var version = versionMatch.Success ? versionMatch.Groups[1].Value : "detected";
                    return (true, "winget:Maltego.Maltego", "maltego", version);
                }
            }
        }

        return (false, null, null, null);
    }

    private static (bool Detected, string? Path, string? CommandName) ResolveExecutable(ToolDefinition definition)
    {
        foreach (var candidate in definition.CommandCandidates)
        {
            var resolved = ResolveFromPath(candidate);
            if (resolved is not null)
            {
                return (true, resolved, candidate);
            }
        }

        foreach (var path in definition.KnownWindowsPaths)
        {
            if (File.Exists(path))
            {
                return (true, path, Path.GetFileName(path));
            }
        }

        return (false, null, null);
    }

    private static string? ResolveFromPath(string commandName)
    {
        if (Path.IsPathRooted(commandName) && File.Exists(commandName))
        {
            return commandName;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        var extensions = pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasExt = Path.HasExtension(commandName);
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (hasExt)
                {
                    var candidate = Path.Combine(dir, commandName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                else
                {
                    foreach (var ext in extensions)
                    {
                        var candidate = Path.Combine(dir, commandName + ext);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }

                    var noExtCandidate = Path.Combine(dir, commandName);
                    if (File.Exists(noExtCandidate))
                    {
                        return noExtCandidate;
                    }
                }
            }
            catch
            {
                // Ignore bad path entries.
            }
        }

        return null;
    }
}
