using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using Membran.MultiTool.Core.Models;
using Microsoft.Win32;

namespace Membran.MultiTool.Uninstall.Services;

[SupportedOSPlatform("windows")]
public sealed class AppDiscoveryService
{
    private static readonly (RegistryHive Hive, string Path)[] RegistryLocations =
    [
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
    ];

    public IReadOnlyList<DiscoveredApp> DiscoverInstalledApps(bool includeSystemComponents = false)
    {
        var byComposite = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, path) in RegistryLocations)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var uninstallRoot = baseKey.OpenSubKey(path);
            if (uninstallRoot is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
            {
                using var appKey = uninstallRoot.OpenSubKey(subKeyName);
                if (appKey is null)
                {
                    continue;
                }

                var displayName = ReadString(appKey, "DisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var publisher = ReadString(appKey, "Publisher");
                var app = new DiscoveredApp
                {
                    AppId = BuildAppId(hive, path, subKeyName),
                    DisplayName = displayName,
                    Publisher = publisher,
                    Version = ReadString(appKey, "DisplayVersion"),
                    UninstallString = ReadString(appKey, "UninstallString"),
                    InstallLocation = ReadString(appKey, "InstallLocation"),
                    IsSystemComponent = IsSystemComponent(appKey, displayName, publisher),
                    RegistryPath = $"{HiveName(hive)}\\{path}\\{subKeyName}"
                };

                var dedupeKey = $"{displayName}|{publisher}";
                byComposite[dedupeKey] = MergeDuplicate(byComposite.GetValueOrDefault(dedupeKey), app);
            }
        }

        var apps = byComposite.Values
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return includeSystemComponents
            ? apps
            : apps.Where(app => !app.IsSystemComponent).ToList();
    }

    private static DiscoveredApp MergeDuplicate(DiscoveredApp? existing, DiscoveredApp candidate)
    {
        if (existing is null)
        {
            return candidate;
        }

        var existingScore = Score(existing);
        var candidateScore = Score(candidate);
        return candidateScore >= existingScore ? candidate : existing;
    }

    private static int Score(DiscoveredApp app)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(app.UninstallString))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(app.Version))
        {
            score += 1;
        }

        return score;
    }

    private static bool IsSystemComponent(RegistryKey appKey, string displayName, string publisher)
    {
        var systemComponent = appKey.GetValue("SystemComponent");
        if (systemComponent is int intValue && intValue == 1)
        {
            return true;
        }

        var releaseType = ReadString(appKey, "ReleaseType");
        if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            if (displayName.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Update", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadString(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName)?.ToString()?.Trim() ?? string.Empty;
    }

    private static string BuildAppId(RegistryHive hive, string path, string subKey)
    {
        using var sha = SHA256.Create();
        var input = $"{HiveName(hive)}|{path}|{subKey}";
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
    }

    private static string HiveName(RegistryHive hive)
    {
        return hive switch
        {
            RegistryHive.LocalMachine => "HKLM",
            RegistryHive.CurrentUser => "HKCU",
            _ => hive.ToString()
        };
    }
}
