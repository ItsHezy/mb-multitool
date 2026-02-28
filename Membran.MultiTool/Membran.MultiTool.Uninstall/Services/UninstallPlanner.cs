using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Windows;
using Microsoft.Win32;

namespace Membran.MultiTool.Uninstall.Services;

[SupportedOSPlatform("windows")]
public sealed class UninstallPlanner(PathGuard pathGuard, SystemCommandRunner commandRunner)
{
    private const string ArtifactFile = "file";
    private const string ArtifactRegistryKey = "registry-key";
    private const string ArtifactStartupValue = "startup-registry-value";
    private const string ArtifactService = "service";
    private const string ArtifactScheduledTask = "scheduled-task";
    private const string ArtifactProcess = "process";

    public async Task<UninstallPlan> CreatePlanAsync(
        DiscoveredApp app,
        ManualRule? manualRule,
        bool restorePointPlanned = true,
        CancellationToken cancellationToken = default)
    {
        manualRule ??= ManualRule.Empty;
        var artifacts = new List<UninstallArtifact>();
        var tokens = BuildMatchTokens(app, manualRule);

        AddFileArtifacts(app, manualRule, artifacts);
        AddRegistryArtifacts(app, manualRule, artifacts);
        AddStartupRegistryArtifacts(tokens, artifacts);
        AddProcessArtifacts(manualRule, artifacts);
        AddServiceArtifacts(tokens, artifacts);
        await AddScheduledTaskArtifactsAsync(tokens, artifacts, cancellationToken).ConfigureAwait(false);

        var deduped = artifacts
            .GroupBy(item => $"{item.Type}|{item.PathOrKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var requiresAdmin = app.RegistryPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ||
                            deduped.Any(IsPrivilegedArtifact);

        return new UninstallPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            AppId = app.AppId,
            DisplayName = app.DisplayName,
            UninstallString = app.UninstallString,
            Artifacts = deduped,
            RequiresAdmin = requiresAdmin,
            RestorePointPlanned = restorePointPlanned
        };
    }

    private void AddFileArtifacts(DiscoveredApp app, ManualRule manualRule, List<UninstallArtifact> artifacts)
    {
        var candidates = new List<(string Path, string Evidence, string Risk)>
        {
            (app.InstallLocation ?? string.Empty, "InstallLocation from uninstall registry.", "high"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), app.DisplayName), "CommonApplicationData candidate.", "medium"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), app.DisplayName), "LocalApplicationData candidate.", "medium"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app.DisplayName), "ApplicationData candidate.", "medium"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", app.DisplayName), "Common start menu entry.", "low"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", app.DisplayName), "User start menu entry.", "low")
        };

        foreach (var manualPath in manualRule.FilePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            candidates.Add((manualPath, "Manual file rule.", "high"));
        }

        foreach (var (path, evidence, risk) in candidates)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = SafeNormalize(path);
            if (normalized is null)
            {
                continue;
            }

            var withinAllowedRoots = pathGuard.IsAllowed(normalized);
            var isManualCandidate = evidence.StartsWith("Manual", StringComparison.OrdinalIgnoreCase);
            if (!withinAllowedRoots && !isManualCandidate)
            {
                continue;
            }

            if (!Directory.Exists(normalized) && !File.Exists(normalized) && !isManualCandidate)
            {
                continue;
            }

            var finalEvidence = withinAllowedRoots
                ? evidence
                : $"{evidence} Outside allowed roots; execution safety gate will block unless policy changes.";

            artifacts.Add(new UninstallArtifact
            {
                ArtifactId = BuildArtifactId(ArtifactFile, normalized),
                Type = ArtifactFile,
                PathOrKey = normalized,
                Evidence = finalEvidence,
                RiskLevel = risk
            });
        }
    }

    private static void AddRegistryArtifacts(DiscoveredApp app, ManualRule manualRule, List<UninstallArtifact> artifacts)
    {
        var candidates = new List<string>
        {
            $@"HKCU\Software\{app.DisplayName}",
            $@"HKLM\Software\{app.DisplayName}"
        };

        if (!string.IsNullOrWhiteSpace(app.Publisher))
        {
            candidates.Add($@"HKCU\Software\{app.Publisher}\{app.DisplayName}");
            candidates.Add($@"HKLM\Software\{app.Publisher}\{app.DisplayName}");
        }

        candidates.AddRange(manualRule.RegistryPaths.Where(path => !string.IsNullOrWhiteSpace(path)));

        foreach (var candidate in candidates)
        {
            artifacts.Add(new UninstallArtifact
            {
                ArtifactId = BuildArtifactId(ArtifactRegistryKey, candidate),
                Type = ArtifactRegistryKey,
                PathOrKey = candidate,
                Evidence = "Generated registry candidate.",
                RiskLevel = "high"
            });
        }
    }

    private static void AddProcessArtifacts(ManualRule manualRule, List<UninstallArtifact> artifacts)
    {
        foreach (var processName in manualRule.ProcessNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            artifacts.Add(new UninstallArtifact
            {
                ArtifactId = BuildArtifactId(ArtifactProcess, processName),
                Type = ArtifactProcess,
                PathOrKey = processName,
                Evidence = "Manual process cleanup rule.",
                RiskLevel = "medium"
            });
        }
    }

    private static void AddServiceArtifacts(HashSet<string> tokens, List<UninstallArtifact> artifacts)
    {
        foreach (var service in ServiceController.GetServices())
        {
            var serviceName = service.ServiceName;
            var displayName = service.DisplayName;

            if (!MatchesTokens(tokens, serviceName, displayName))
            {
                continue;
            }

            artifacts.Add(new UninstallArtifact
            {
                ArtifactId = BuildArtifactId(ArtifactService, serviceName),
                Type = ArtifactService,
                PathOrKey = serviceName,
                Evidence = $"Matched service name/display name: {displayName}",
                RiskLevel = "high"
            });
        }
    }

    private async Task AddScheduledTaskArtifactsAsync(
        HashSet<string> tokens,
        List<UninstallArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner
            .RunAsync("schtasks.exe", "/Query /FO CSV /NH", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
        {
            return;
        }

        foreach (var rawLine in result.StdOut.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var taskName = ExtractCsvFirstColumn(rawLine);
            if (string.IsNullOrWhiteSpace(taskName))
            {
                continue;
            }

            if (!MatchesTokens(tokens, taskName))
            {
                continue;
            }

            artifacts.Add(new UninstallArtifact
            {
                ArtifactId = BuildArtifactId(ArtifactScheduledTask, taskName),
                Type = ArtifactScheduledTask,
                PathOrKey = taskName,
                Evidence = "Matched scheduled task name.",
                RiskLevel = "medium"
            });
        }
    }

    private static void AddStartupRegistryArtifacts(HashSet<string> tokens, List<UninstallArtifact> artifacts)
    {
        var startupKeys = new[]
        {
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };

        foreach (var keyPath in startupKeys)
        {
            if (!TryOpenRegistryPath(keyPath, out var hive, out var subKey))
            {
                continue;
            }

            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var runKey = baseKey.OpenSubKey(subKey);
            if (runKey is null)
            {
                continue;
            }

            foreach (var valueName in runKey.GetValueNames())
            {
                var valueData = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                if (!MatchesTokens(tokens, valueName, valueData))
                {
                    continue;
                }

                artifacts.Add(new UninstallArtifact
                {
                    ArtifactId = BuildArtifactId(ArtifactStartupValue, $"{keyPath}::{valueName}"),
                    Type = ArtifactStartupValue,
                    PathOrKey = $"{keyPath}::{valueName}",
                    Evidence = "Matched startup Run value name/data.",
                    RiskLevel = "high"
                });
            }
        }
    }

    private static HashSet<string> BuildMatchTokens(DiscoveredApp app, ManualRule manualRule)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app",
            "apps",
            "service",
            "services",
            "manual",
            "input",
            "program",
            "programs",
            "setup",
            "install",
            "installer",
            "uninstall",
            "tool",
            "tools",
            "update"
        };

        var tokenSource = string.Join(
            ' ',
            app.DisplayName,
            app.Publisher,
            manualRule.NamePattern);

        var tokens = tokenSource
            .Split([' ', '-', '_', '.', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 4)
            .Where(token => !stopWords.Contains(token))
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(app.DisplayName))
        {
            tokens.Add(app.DisplayName.ToLowerInvariant());
        }

        return tokens;
    }

    private static bool MatchesTokens(HashSet<string> tokens, params string[] values)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var joined = string.Join(' ', values).ToLowerInvariant();
        return tokens.Any(joined.Contains);
    }

    private static string BuildArtifactId(string type, string pathOrKey)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{type}|{pathOrKey}"));
        return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
    }

    private bool IsPrivilegedArtifact(UninstallArtifact artifact)
    {
        if (artifact.Type is ArtifactService or ArtifactScheduledTask)
        {
            return true;
        }

        if (artifact.Type is ArtifactRegistryKey or ArtifactStartupValue)
        {
            return artifact.PathOrKey.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
        }

        if (artifact.Type == ArtifactFile)
        {
            var normalized = SafeNormalize(artifact.PathOrKey);
            return normalized is not null && !IsUserWritablePath(normalized);
        }

        return false;
    }

    private static bool IsUserWritablePath(string normalized)
    {
        var writableRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Path.GetTempPath()
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath);

        return writableRoots.Any(root =>
            normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractCsvFirstColumn(string csvLine)
    {
        if (string.IsNullOrWhiteSpace(csvLine))
        {
            return string.Empty;
        }

        if (!csvLine.StartsWith('"'))
        {
            return csvLine.Split(',', 2)[0].Trim();
        }

        var sb = new StringBuilder();
        for (var i = 1; i < csvLine.Length; i++)
        {
            var current = csvLine[i];
            if (current == '"')
            {
                if (i + 1 < csvLine.Length && csvLine[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                    continue;
                }

                break;
            }

            sb.Append(current);
        }

        return sb.ToString();
    }

    private static bool TryOpenRegistryPath(string fullPath, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.CurrentUser;
        subKey = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var split = fullPath.Split('\\', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length != 2)
        {
            return false;
        }

        hive = split[0].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            _ => RegistryHive.CurrentUser
        };
        if (hive != RegistryHive.CurrentUser && hive != RegistryHive.LocalMachine)
        {
            return false;
        }

        subKey = split[1];
        return !string.IsNullOrWhiteSpace(subKey);
    }

    private static string? SafeNormalize(string path)
    {
        try
        {
            return PathGuard.ExpandAndNormalize(path);
        }
        catch
        {
            return null;
        }
    }
}
