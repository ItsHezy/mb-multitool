using System.Diagnostics;
using System.Text.RegularExpressions;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Windows;

namespace Membran.MultiTool.Osint.Services;

public interface IToolRunner
{
    Task<ToolExecutionResult> RunAsync(
        ToolAvailability availability,
        OsintTargetType targetType,
        string targetValue,
        int timeoutSec,
        bool includeRaw,
        CancellationToken cancellationToken = default);
}

public sealed class ToolRunner(SystemCommandRunner commandRunner, ToolRegistry toolRegistry) : IToolRunner
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"(?<![\w.+-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}(?![\w.-])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", RegexOptions.Compiled);
    private static readonly Regex DomainRegex = new(@"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\+?\d[\d\s().-]{6,}\d", RegexOptions.Compiled);

    private readonly SystemCommandRunner _commandRunner = commandRunner;
    private readonly ToolRegistry _toolRegistry = toolRegistry;

    public async Task<ToolExecutionResult> RunAsync(
        ToolAvailability availability,
        OsintTargetType targetType,
        string targetValue,
        int timeoutSec,
        bool includeRaw,
        CancellationToken cancellationToken = default)
    {
        if (!_toolRegistry.TryGetDefinition(availability.ToolName, out var definition))
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = false,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = "Unknown tool definition."
            };
        }

        if (!definition.Capabilities.Contains(targetType))
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = false,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = $"Tool '{availability.ToolName}' does not support target type '{targetType}'."
            };
        }

        if (!availability.Detected)
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = false,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = "Tool not detected on this machine.",
                Findings = Array.Empty<OsintFinding>()
            };
        }

        if (definition.ManualOnly || availability.ToolName is "recon-ng" or "spiderfoot")
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = false,
                Succeeded = true,
                ExitCode = 0,
                ParsedSummary = $"'{availability.ToolName}' detected. v1 uses manual-step integration for this tool.",
                Findings = Array.Empty<OsintFinding>()
            };
        }

        var invocation = BuildInvocation(availability, targetType, targetValue);
        if (invocation is null)
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = false,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = $"No invocation available for '{availability.ToolName}' and target type '{targetType}'.",
                Findings = Array.Empty<OsintFinding>()
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await _commandRunner
            .RunAsync(invocation.FileName, invocation.Arguments, timeoutSec * 1_000, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var timedOut = result.ExitCode == -1 &&
                       result.StdErr.Contains("Timed out.", StringComparison.OrdinalIgnoreCase);
        var output = string.Join(Environment.NewLine, result.StdOut, result.StdErr);
        var findings = ExtractFindings(availability.ToolName, output);
        var summary = BuildSummary(result.ExitCode, timedOut, findings, output);

        return new ToolExecutionResult
        {
            ToolName = availability.ToolName,
            Executed = true,
            Succeeded = result.ExitCode == 0 && !timedOut,
            TimedOut = timedOut,
            ExitCode = result.ExitCode,
            DurationMs = stopwatch.ElapsedMilliseconds,
            ParsedSummary = summary,
            RawStdOut = includeRaw ? result.StdOut : string.Empty,
            RawStdErr = includeRaw ? result.StdErr : string.Empty,
            Findings = findings
        };
    }

    private ToolInvocation? BuildInvocation(ToolAvailability availability, OsintTargetType targetType, string targetValue)
    {
        var toolName = availability.ToolName.ToLowerInvariant();
        var target = targetValue.Trim();

        return toolName switch
        {
            "sherlock" => new ToolInvocation(availability.Path!, $"\"{target}\" --print-found"),
            "phoneinfoga" => new ToolInvocation(availability.Path!, $"scan -n \"{target}\""),
            "holehe" => new ToolInvocation(availability.Path!, $"\"{target}\""),
            "maigret" => new ToolInvocation(availability.Path!, $"\"{target}\" --json"),
            "theharvester" => BuildTheHarvesterInvocation(availability.Path!, targetType, target),
            "bbot" => new ToolInvocation(availability.Path!, $"-t \"{target}\" --silent"),
            "osrframework" => BuildOsrInvocation(targetType, target),
            _ => null
        };
    }

    private static ToolInvocation? BuildTheHarvesterInvocation(string executablePath, OsintTargetType targetType, string targetValue)
    {
        var domain = targetType switch
        {
            OsintTargetType.Domain => targetValue,
            OsintTargetType.Email => targetValue.Contains('@', StringComparison.Ordinal)
                ? targetValue.Split('@', 2)[1]
                : string.Empty,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        return new ToolInvocation(executablePath, $"-d \"{domain}\" -b all");
    }

    private static ToolInvocation? BuildOsrInvocation(OsintTargetType targetType, string targetValue)
    {
        var command = targetType switch
        {
            OsintTargetType.Username => "usufy",
            OsintTargetType.Email => "mailfy",
            OsintTargetType.Domain => "domainfy",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var executable = ResolveCommandFromPath(command);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        return new ToolInvocation(executable, $"\"{targetValue}\"");
    }

    private static OsintFinding[] ExtractFindings(string source, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<OsintFinding>();
        }

        var findings = new List<OsintFinding>();

        findings.AddRange(UrlRegex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(url => new OsintFinding
            {
                Source = source,
                Type = "url",
                Indicator = url,
                Confidence = "low",
                Context = "Extracted from tool output."
            }));

        findings.AddRange(EmailRegex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(email => new OsintFinding
            {
                Source = source,
                Type = "email",
                Indicator = email,
                Confidence = "low",
                Context = "Extracted from tool output."
            }));

        findings.AddRange(IpRegex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ip => new OsintFinding
            {
                Source = source,
                Type = "ip",
                Indicator = ip,
                Confidence = "low",
                Context = "Extracted from tool output."
            }));

        findings.AddRange(DomainRegex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(domain => new OsintFinding
            {
                Source = source,
                Type = "domain",
                Indicator = domain,
                Confidence = "low",
                Context = "Extracted from tool output."
            }));

        findings.AddRange(PhoneRegex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(phone => new OsintFinding
            {
                Source = source,
                Type = "phone",
                Indicator = phone,
                Confidence = "low",
                Context = "Extracted from tool output."
            }));

        return findings
            .GroupBy(item => $"{item.Type}|{item.Indicator}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string BuildSummary(int exitCode, bool timedOut, IReadOnlyCollection<OsintFinding> findings, string rawText)
    {
        if (timedOut)
        {
            return "Timed out.";
        }

        if (exitCode != 0)
        {
            return $"Command exited with code {exitCode}.";
        }

        if (findings.Count > 0)
        {
            return $"Collected {findings.Count} normalized finding(s).";
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "No output returned.";
        }

        var firstLine = rawText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine)
            ? "Completed successfully with no parseable findings."
            : firstLine.Length > 140 ? firstLine[..140] : firstLine;
    }

    private static string? ResolveCommandFromPath(string commandName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        var exts = pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                foreach (var ext in exts)
                {
                    var candidate = Path.Combine(dir, commandName + ext);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                var noExt = Path.Combine(dir, commandName);
                if (File.Exists(noExt))
                {
                    return noExt;
                }
            }
            catch
            {
                // Ignore invalid path segments.
            }
        }

        return null;
    }

    private sealed record ToolInvocation(string FileName, string Arguments);
}
