using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Services;
using Membran.MultiTool.Core.Windows;
using Membran.MultiTool.Geo.Services;
using Membran.MultiTool.Uninstall.Services;

namespace Membran.MultiTool.Cli;

public static class CliRuntime
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string[] NormalizeShortcutArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return args;
        }

        if (IsAlias(args[0], "-IP", "--IP", "/IP", "/ip"))
        {
            return ["ip", .. args.Skip(1)];
        }

        if (IsAlias(args[0], "-ui", "--ui", "/ui", "/UI"))
        {
            return ["ui", .. args.Skip(1)];
        }

        return args;
    }

    public static bool ResolveJsonOutput(bool parsedValue, bool explicitOptionProvided, MbConfig config)
    {
        if (explicitOptionProvided)
        {
            return parsedValue;
        }

        return config.OutputFormat.Equals("json", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> HandleGeoLookupAsync(GeoLookupService geoLookupService, string input, bool asJson)
    {
        try
        {
            var result = await geoLookupService
                .LookupAsync(new GeoQuery { InputIpOrSelf = input })
                .ConfigureAwait(false);
            Output(result, asJson, FormatGeoResult);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Geo lookup failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> HandleIpBatchAsync(
        GeoLookupService geoLookupService,
        string filePath,
        bool asJson,
        JsonSerializerOptions jsonOptions)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(filePath.Trim());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid file path '{filePath}': {ex.Message}");
            return 1;
        }

        if (!File.Exists(normalizedPath))
        {
            Console.Error.WriteLine($"File not found: {normalizedPath}");
            return 1;
        }

        var inputs = File.ReadAllLines(normalizedPath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No IP entries found in file.");
            return 1;
        }

        var results = new List<BatchLookupResult>();
        foreach (var input in inputs)
        {
            try
            {
                var result = await geoLookupService
                    .LookupAsync(new GeoQuery { InputIpOrSelf = input })
                    .ConfigureAwait(false);

                results.Add(new BatchLookupResult
                {
                    Input = input,
                    Success = true,
                    Result = result
                });
            }
            catch (Exception ex)
            {
                results.Add(new BatchLookupResult
                {
                    Input = input,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(results, jsonOptions));
        }
        else
        {
            Console.WriteLine($"Batch lookup file: {normalizedPath}");
            Console.WriteLine($"Total: {results.Count}, Success: {results.Count(item => item.Success)}, Failed: {results.Count(item => !item.Success)}");

            foreach (var item in results)
            {
                if (item.Success && item.Result is not null)
                {
                    Console.WriteLine($" - {item.Input}: {item.Result.Country}/{item.Result.Region}/{item.Result.City} via {item.Result.SourceProvider}");
                }
                else
                {
                    Console.WriteLine($" - {item.Input}: ERROR {item.Error}");
                }
            }
        }

        return results.Any(item => !item.Success) ? 1 : 0;
    }

    public static async Task<int> HandleUiCommandAsync(
        string rawPath,
        bool autoYes,
        bool dryRun,
        bool asJson,
        bool attemptRestorePoint,
        string[] excludePatterns,
        bool elevate,
        bool elevatedInternal,
        AppDiscoveryService appDiscoveryService,
        UninstallPlanner uninstallPlanner,
        UninstallExecutor uninstallExecutor,
        PersistenceStore persistenceStore,
        JsonSerializerOptions jsonOptions)
    {
        string normalizedPath;
        try
        {
            normalizedPath = NormalizeUserPath(rawPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid path '{rawPath}': {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(normalizedPath) && !File.Exists(normalizedPath))
        {
            Console.Error.WriteLine($"Path not found: {normalizedPath}");
            return 1;
        }

        if (IsUnsafeQuickTarget(normalizedPath))
        {
            Console.Error.WriteLine($"Blocked unsafe target path: {normalizedPath}");
            Console.Error.WriteLine("Pick an app-specific file/folder path, not a top-level system/user root.");
            return 1;
        }

        if (!autoYes)
        {
            Console.WriteLine("Deep cleanup cannot guarantee forensic no-trace removal.");
            Console.WriteLine("It will remove detected app artifacts only.");
            Console.Write($"Type YES to continue cleaning '{normalizedPath}': ");
            var confirmation = Console.ReadLine();
            if (!string.Equals(confirmation, "YES", StringComparison.Ordinal))
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }

        try
        {
            var discoveredApps = appDiscoveryService.DiscoverInstalledApps(includeSystemComponents: true);
            var matchedApp = discoveredApps.FirstOrDefault(app => InstallLocationMatches(app.InstallLocation, normalizedPath));
            var targetApp = matchedApp ?? CreatePathOnlyApp(normalizedPath);

            var processName = Path.GetFileNameWithoutExtension(normalizedPath);
            var processNames = File.Exists(normalizedPath) &&
                               normalizedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(processName)
                ? [processName]
                : Array.Empty<string>();

            var manualRule = new ManualRule
            {
                NamePattern = matchedApp is null ? string.Empty : targetApp.DisplayName,
                FilePaths = [normalizedPath],
                ProcessNames = processNames
            };

            var planned = await uninstallPlanner
                .CreatePlanAsync(targetApp, manualRule, restorePointPlanned: attemptRestorePoint)
                .ConfigureAwait(false);

            var (plan, excludedArtifacts) = ApplyUiExclusions(planned, excludePatterns);
            persistenceStore.SavePlan(plan);

            if (elevate && !dryRun && !AdminContext.IsElevated() && plan.RequiresAdmin && !elevatedInternal)
            {
                if (TryRelaunchAsAdminWithCurrentArgs(out var message))
                {
                    Console.WriteLine(message);
                    return 0;
                }

                Console.Error.WriteLine(message);
                return 1;
            }

            var report = await uninstallExecutor
                .ExecuteAsync(
                    plan,
                    new UninstallExecutionOptions
                    {
                        Confirm = true,
                        DryRun = dryRun,
                        AttemptRestorePoint = attemptRestorePoint,
                        AllowPartialWithoutAdmin = true
                    })
                .ConfigureAwait(false);
            persistenceStore.SaveReport(report);

            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    Shortcut = "ui",
                    TargetPath = normalizedPath,
                    ExcludedArtifacts = excludedArtifacts,
                    Plan = plan,
                    Report = report
                }, jsonOptions));
            }
            else
            {
                Console.WriteLine($"Target path:     {normalizedPath}");
                Console.WriteLine($"PlanId:          {plan.PlanId}");
                Console.WriteLine($"Artifacts:       {plan.Artifacts.Length}");
                Console.WriteLine($"ExcludedCount:   {excludedArtifacts.Length}");
                Console.WriteLine($"RequiresAdmin:   {plan.RequiresAdmin}");
                Console.WriteLine($"RemovedCount:    {report.RemovedCount}");
                Console.WriteLine($"FailedCount:     {report.FailedCount}");
                Console.WriteLine($"SkippedCount:    {report.SkippedCount}");
                if (report.Failures.Length > 0)
                {
                    Console.WriteLine("Failures:");
                    foreach (var failure in report.Failures)
                    {
                        Console.WriteLine($" - {failure}");
                    }
                }

                if (report.Skipped.Length > 0)
                {
                    Console.WriteLine("Skipped:");
                    foreach (var skipped in report.Skipped)
                    {
                        Console.WriteLine($" - {skipped}");
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UI cleanup failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> HandleDoctorAsync(
        bool asJson,
        ConfigStore configStore,
        PersistenceStore persistenceStore,
        JsonSerializerOptions jsonOptions)
    {
        var checks = new List<DoctorCheck>();

        var processPath = Environment.ProcessPath ?? string.Empty;
        var processPathOk = !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath);
        checks.Add(new DoctorCheck("command_path", processPathOk ? "pass" : "fail", processPathOk ? processPath : "Process path unavailable."));

        var pathOk = IsMbReachableOnPath();
        checks.Add(new DoctorCheck(
            "path_resolution",
            pathOk ? "pass" : "fail",
            pathOk ? "mb command appears on PATH." : "mb command was not found on current PATH."));

        var isElevated = AdminContext.IsElevated();
        checks.Add(new DoctorCheck(
            "admin_context",
            isElevated ? "pass" : "warn",
            isElevated ? "Running with Administrator privileges." : "Running without Administrator privileges."));

        var dataStoreWritable = TryWriteProbeFile(persistenceStore.RootDirectory, out var dataStoreMessage);
        checks.Add(new DoctorCheck("data_store", dataStoreWritable ? "pass" : "fail", dataStoreMessage));

        var configReadWrite = TryRoundTripConfig(configStore, out var configMessage);
        checks.Add(new DoctorCheck("config_store", configReadWrite ? "pass" : "fail", configMessage));

        var ipInfoDns = await TryResolveHostAsync("ipinfo.io").ConfigureAwait(false);
        checks.Add(new DoctorCheck(
            "dns_ipinfo_io",
            ipInfoDns ? "pass" : "warn",
            ipInfoDns ? "DNS resolution succeeded." : "DNS resolution failed."));

        var ipApiDns = await TryResolveHostAsync("ipapi.co").ConfigureAwait(false);
        checks.Add(new DoctorCheck(
            "dns_ipapi_co",
            ipApiDns ? "pass" : "warn",
            ipApiDns ? "DNS resolution succeeded." : "DNS resolution failed."));

        var summary = new
        {
            pass = checks.Count(check => check.Status == "pass"),
            warn = checks.Count(check => check.Status == "warn"),
            fail = checks.Count(check => check.Status == "fail")
        };

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                summary,
                checks
            }, jsonOptions));
        }
        else
        {
            Console.WriteLine("Doctor checks:");
            foreach (var check in checks)
            {
                Console.WriteLine($" - [{check.Status.ToUpperInvariant()}] {check.Name}: {check.Message}");
            }

            Console.WriteLine($"Summary: pass={summary.pass}, warn={summary.warn}, fail={summary.fail}");
        }

        return summary.fail > 0 ? 1 : 0;
    }

    public static string NormalizeConfigKey(string key)
    {
        return key.Trim().ToLowerInvariant().Replace('-', '_');
    }

    public static ManualRule LoadManualRule(string? manualRulePath, JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(manualRulePath))
        {
            return ManualRule.Empty;
        }

        var normalized = Path.GetFullPath(manualRulePath);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"Manual rule file not found: {normalized}");
        }

        var rule = JsonSerializer.Deserialize<ManualRule>(File.ReadAllText(normalized), jsonOptions);
        return rule ?? ManualRule.Empty;
    }

    public static void Output<T>(T value, bool asJson, Func<T, string> formatter)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, DefaultJsonOptions));
            return;
        }

        Console.WriteLine(formatter(value));
    }

    public static string FormatGeoResult(GeoResult result)
    {
        return $"""
            Query IP:      {result.QueryIp}
            Provider:      {result.SourceProvider}
            Country:       {result.Country}
            Region:        {result.Region}
            City:          {result.City}
            Coordinates:   {result.Latitude}, {result.Longitude}
            ISP:           {result.Isp}
            Accuracy:      {result.AccuracyLabel}
            Confidence:    {result.Confidence:P0}
            Retrieved At:  {result.RetrievedAt:O}
            """;
    }

    public static string FormatApps(IReadOnlyList<DiscoveredApp> apps)
    {
        if (apps.Count == 0)
        {
            return "No apps discovered.";
        }

        var lines = new List<string>
        {
            $"Discovered apps: {apps.Count}",
            "AppId             Name                                 Publisher                    Version"
        };

        lines.AddRange(apps.Select(app =>
            $"{app.AppId,-16} {Truncate(app.DisplayName, 36),-36} {Truncate(app.Publisher, 28),-28} {app.Version ?? "-"}"));

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatPlanResult(UninstallPlan plan)
    {
        var lines = new List<string>
        {
            $"PlanId:            {plan.PlanId}",
            $"AppId:             {plan.AppId}",
            $"DisplayName:       {plan.DisplayName}",
            $"RequiresAdmin:     {plan.RequiresAdmin}",
            $"RestorePointPlan:  {plan.RestorePointPlanned}",
            $"Artifacts:         {plan.Artifacts.Length}"
        };

        lines.AddRange(plan.Artifacts.Select(artifact =>
            $" - [{artifact.RiskLevel}] {artifact.Type}: {artifact.PathOrKey}"));

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatReportResult(ExecutionReport report)
    {
        var lines = new List<string>
        {
            $"PlanId:          {report.PlanId}",
            $"RemovedCount:    {report.RemovedCount}",
            $"FailedCount:     {report.FailedCount}",
            $"SkippedCount:    {report.SkippedCount}",
            $"StartedAt:       {report.StartedAt:O}",
            $"CompletedAt:     {report.CompletedAt:O}"
        };

        if (report.Failures.Length > 0)
        {
            lines.Add("Failures:");
            lines.AddRange(report.Failures.Select(failure => $" - {failure}"));
        }

        if (report.Skipped.Length > 0)
        {
            lines.Add("Skipped:");
            lines.AddRange(report.Skipped.Select(item => $" - {item}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static (UninstallPlan FilteredPlan, UninstallArtifact[] ExcludedArtifacts) ApplyUiExclusions(
        UninstallPlan plan,
        string[] patterns)
    {
        var normalizedPatterns = patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPatterns.Length == 0)
        {
            return (plan, Array.Empty<UninstallArtifact>());
        }

        var excluded = plan.Artifacts
            .Where(artifact => normalizedPatterns.Any(pattern => MatchesExcludePattern(artifact, pattern)))
            .ToArray();

        if (excluded.Length == 0)
        {
            return (plan, Array.Empty<UninstallArtifact>());
        }

        var filteredArtifacts = plan.Artifacts
            .Where(artifact => !excluded.Any(x => x.ArtifactId == artifact.ArtifactId))
            .ToArray();

        var requiresAdmin = filteredArtifacts.Any(IsLikelyAdminOnlyArtifact);

        var filteredPlan = new UninstallPlan
        {
            PlanId = plan.PlanId,
            AppId = plan.AppId,
            DisplayName = plan.DisplayName,
            UninstallString = plan.UninstallString,
            Artifacts = filteredArtifacts,
            RequiresAdmin = requiresAdmin,
            RestorePointPlanned = plan.RestorePointPlanned,
            CreatedAt = plan.CreatedAt
        };

        return (filteredPlan, excluded);
    }

    private static bool MatchesExcludePattern(UninstallArtifact artifact, string pattern)
    {
        var candidates = new[]
        {
            artifact.PathOrKey,
            artifact.Type,
            artifact.Evidence,
            artifact.ArtifactId
        };

        var hasWildcard = pattern.Contains('*') || pattern.Contains('?');
        if (!hasWildcard)
        {
            return candidates.Any(candidate =>
                candidate.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return candidates.Any(candidate => regex.IsMatch(candidate));
    }

    private static bool IsLikelyAdminOnlyArtifact(UninstallArtifact artifact)
    {
        if (artifact.Type is "service" or "scheduled-task")
        {
            return true;
        }

        if (artifact.Type is "registry-key" or "startup-registry-value")
        {
            return artifact.PathOrKey.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
        }

        if (artifact.Type == "file")
        {
            try
            {
                var normalized = PathGuard.ExpandAndNormalize(artifact.PathOrKey);
                return !IsUserWritablePath(normalized);
            }
            catch
            {
                return false;
            }
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
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        return writableRoots.Any(root =>
            normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryRelaunchAsAdminWithCurrentArgs(out string message)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            message = "Cannot relaunch as administrator: process path is unavailable.";
            return false;
        }

        var args = Environment.GetCommandLineArgs()
            .Skip(1)
            .ToList();

        if (!args.Any(arg => arg.Equals("--elevated-internal", StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("--elevated-internal");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = BuildCommandLine(args),
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory
        };

        try
        {
            Process.Start(startInfo);
            message = "Elevation requested. Continue in the new Administrator window.";
            return true;
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            message = "Elevation prompt was canceled by the user.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Failed to relaunch as administrator: {ex.Message}";
            return false;
        }
    }

    private static string BuildCommandLine(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"'))
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static bool IsMbReachableOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var segments = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            try
            {
                var normalized = Path.GetFullPath(segment);
                if (File.Exists(Path.Combine(normalized, "mb.cmd")) || File.Exists(Path.Combine(normalized, "mb.exe")))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore invalid PATH segment.
            }
        }

        return false;
    }

    private static bool TryWriteProbeFile(string directory, out string message)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $"probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            message = $"Writable: {directory}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Write probe failed for '{directory}': {ex.Message}";
            return false;
        }
    }

    private static bool TryRoundTripConfig(ConfigStore store, out string message)
    {
        try
        {
            var current = store.Load();
            store.Save(current);
            message = "Config file is readable and writable.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Config read/write failed: {ex.Message}";
            return false;
        }
    }

    private static async Task<bool> TryResolveHostAsync(string host)
    {
        var lookupTask = Dns.GetHostAddressesAsync(host);
        var completed = await Task.WhenAny(lookupTask, Task.Delay(3_000)).ConfigureAwait(false);
        if (completed != lookupTask)
        {
            return false;
        }

        try
        {
            var addresses = await lookupTask.ConfigureAwait(false);
            return addresses.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAlias(string value, params string[] aliases)
    {
        return aliases.Any(alias => value.Equals(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUserPath(string rawPath)
    {
        var input = rawPath.Trim().Trim('"');
        if (input.Length >= 2 && char.IsLetter(input[0]) && (input[1] == '/' || input[1] == '\\'))
        {
            input = $"{input[0]}:{input.Substring(1)}";
        }

        input = Environment.ExpandEnvironmentVariables(input);
        input = input.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(input);
    }

    private static bool InstallLocationMatches(string? installLocation, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        string normalizedInstallLocation;
        try
        {
            normalizedInstallLocation = NormalizeUserPath(installLocation);
        }
        catch
        {
            return false;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return targetPath.Equals(normalizedInstallLocation, comparison) ||
               targetPath.StartsWith(normalizedInstallLocation, comparison) ||
               normalizedInstallLocation.StartsWith(targetPath, comparison);
    }

    private static DiscoveredApp CreatePathOnlyApp(string targetPath)
    {
        var name = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name) || IsWeakQuickName(name))
        {
            name = "ManualTarget";
        }

        return new DiscoveredApp
        {
            AppId = CreatePathAppId(targetPath),
            DisplayName = name,
            Publisher = string.Empty,
            Version = null,
            UninstallString = null,
            InstallLocation = targetPath,
            IsSystemComponent = false,
            RegistryPath = $@"HKCU\Software\{name}"
        };
    }

    private static bool IsWeakQuickName(string value)
    {
        var token = value.Trim().ToLowerInvariant();
        if (token.Length < 4)
        {
            return true;
        }

        return token is "temp" or "tmp" or "cache" or "data" or "files" or "programs" or "program"
            or "windows" or "users" or "user" or "desktop" or "downloads" or "documents";
    }

    private static bool IsUnsafeQuickTarget(string normalizedPath)
    {
        var root = Path.GetPathRoot(normalizedPath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(root) &&
            normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dangerousRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        return dangerousRoots.Any(dangerousRoot =>
            normalizedPath.Equals(dangerousRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreatePathAppId(string path)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }
}

file sealed class BatchLookupResult
{
    public string Input { get; init; } = string.Empty;
    public bool Success { get; init; }
    public GeoResult? Result { get; init; }
    public string? Error { get; init; }
}

file sealed class DoctorCheck
{
    public DoctorCheck(string name, string status, string message)
    {
        Name = name;
        Status = status;
        Message = message;
    }

    public string Name { get; }
    public string Status { get; }
    public string Message { get; }
}
