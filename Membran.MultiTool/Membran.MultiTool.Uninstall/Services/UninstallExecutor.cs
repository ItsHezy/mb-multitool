using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Windows;
using Microsoft.Win32;

namespace Membran.MultiTool.Uninstall.Services;

[SupportedOSPlatform("windows")]
public sealed class UninstallExecutor
{
    private readonly PathGuard _pathGuard;
    private readonly SystemCommandRunner _commandRunner;
    private readonly Func<bool> _isElevatedEvaluator;

    public UninstallExecutor(
        PathGuard pathGuard,
        SystemCommandRunner commandRunner,
        Func<bool>? isElevatedEvaluator = null)
    {
        _pathGuard = pathGuard;
        _commandRunner = commandRunner;
        _isElevatedEvaluator = isElevatedEvaluator ?? AdminContext.IsElevated;
    }

    public async Task<ExecutionReport> ExecuteAsync(
        UninstallPlan plan,
        UninstallExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.Confirm)
        {
            throw new InvalidOperationException("Execution requires explicit confirmation.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var artifactResults = new List<ArtifactExecutionResult>();
        var failures = new List<string>();
        var skipped = new List<string>();
        var isElevated = _isElevatedEvaluator();

        if (plan.RequiresAdmin && !isElevated && !options.DryRun && !options.AllowPartialWithoutAdmin)
        {
            failures.Add("Plan requires administrator privileges. Re-run as Administrator.");
            return BuildReport(plan.PlanId, startedAt, artifactResults, failures, skipped);
        }

        if (plan.RestorePointPlanned && options.AttemptRestorePoint && !options.DryRun)
        {
            if (!isElevated && options.AllowPartialWithoutAdmin)
            {
                var restorePointSkip = "Skipped restore point creation (requires Administrator).";
                skipped.Add(restorePointSkip);
                artifactResults.Add(CreateSkippedResult("restore-point", "restore-point", "system", restorePointSkip));
            }
            else
            {
                var restorePointResult = await TryCreateRestorePointAsync(cancellationToken).ConfigureAwait(false);
                if (!restorePointResult.success)
                {
                    failures.Add(restorePointResult.message);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.UninstallString))
        {
            var uninstallResult = await ExecuteNativeUninstallerAsync(plan.UninstallString, options.DryRun, cancellationToken)
                .ConfigureAwait(false);
            artifactResults.Add(uninstallResult);
            if (!uninstallResult.Removed && !uninstallResult.Skipped)
            {
                failures.Add(uninstallResult.Message);
            }
        }

        foreach (var artifact in plan.Artifacts)
        {
            if (!isElevated && options.AllowPartialWithoutAdmin && IsAdminOnlyArtifact(artifact))
            {
                var skipMessage = $"Skipped admin-only artifact '{artifact.Type}: {artifact.PathOrKey}'.";
                skipped.Add(skipMessage);
                artifactResults.Add(CreateSkippedResult(artifact.ArtifactId, artifact.Type, artifact.PathOrKey, skipMessage));
                continue;
            }

            var result = await ExecuteArtifactAsync(artifact, options.DryRun, cancellationToken).ConfigureAwait(false);
            artifactResults.Add(result);
            if (!result.Removed && !result.Skipped)
            {
                failures.Add(result.Message);
            }
        }

        return BuildReport(plan.PlanId, startedAt, artifactResults, failures, skipped);
    }

    private async Task<ArtifactExecutionResult> ExecuteNativeUninstallerAsync(
        string uninstallString,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return new ArtifactExecutionResult
            {
                ArtifactId = "native-uninstaller",
                Type = "native-uninstaller",
                Target = uninstallString,
                Removed = true,
                Message = "Dry-run: native uninstaller would be executed."
            };
        }

        var result = await _commandRunner
            .RunAsync("cmd.exe", $"/C {uninstallString}", 300_000, cancellationToken)
            .ConfigureAwait(false);

        return new ArtifactExecutionResult
        {
            ArtifactId = "native-uninstaller",
            Type = "native-uninstaller",
            Target = uninstallString,
            Removed = result.ExitCode == 0,
            Message = result.ExitCode == 0
                ? "Native uninstaller completed."
                : $"Native uninstaller failed (exit {result.ExitCode}): {result.StdErr}".Trim()
        };
    }

    private async Task<ArtifactExecutionResult> ExecuteArtifactAsync(
        UninstallArtifact artifact,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        return artifact.Type switch
        {
            "file" => ExecuteFileArtifact(artifact, dryRun),
            "registry-key" => ExecuteRegistryKeyArtifact(artifact, dryRun),
            "startup-registry-value" => ExecuteStartupRegistryValueArtifact(artifact, dryRun),
            "service" => await ExecuteServiceArtifactAsync(artifact, dryRun, cancellationToken).ConfigureAwait(false),
            "scheduled-task" => await ExecuteScheduledTaskArtifactAsync(artifact, dryRun, cancellationToken).ConfigureAwait(false),
            "process" => ExecuteProcessArtifact(artifact, dryRun),
            _ => new ArtifactExecutionResult
            {
                ArtifactId = artifact.ArtifactId,
                Type = artifact.Type,
                Target = artifact.PathOrKey,
                Removed = false,
                Message = $"Unsupported artifact type '{artifact.Type}'."
            }
        };
    }

    private ArtifactExecutionResult ExecuteFileArtifact(UninstallArtifact artifact, bool dryRun)
    {
        string normalized;
        try
        {
            normalized = PathGuard.ExpandAndNormalize(artifact.PathOrKey);
        }
        catch (Exception ex)
        {
            return Failure(artifact, $"Invalid path '{artifact.PathOrKey}': {ex.Message}");
        }

        if (!_pathGuard.IsAllowed(normalized))
        {
            return Failure(artifact, $"Blocked by safety policy (outside allowed roots): {normalized}");
        }

        var exists = Directory.Exists(normalized) || File.Exists(normalized);
        if (!exists)
        {
            return Failure(artifact, $"Path not found: {normalized}");
        }

        if (dryRun)
        {
            return Success(artifact, $"Dry-run: would remove '{normalized}'.");
        }

        try
        {
            if (Directory.Exists(normalized))
            {
                Directory.Delete(normalized, recursive: true);
            }
            else
            {
                File.Delete(normalized);
            }

            return Success(artifact, $"Removed '{normalized}'.");
        }
        catch (Exception ex)
        {
            return Failure(artifact, $"Failed to delete '{normalized}': {ex.Message}");
        }
    }

    private ArtifactExecutionResult ExecuteRegistryKeyArtifact(UninstallArtifact artifact, bool dryRun)
    {
        if (!TryParseRegistryPath(artifact.PathOrKey, out var hive, out var subKey))
        {
            return Failure(artifact, $"Invalid registry key path: {artifact.PathOrKey}");
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key is null)
            {
                return Failure(artifact, $"Registry key not found: {artifact.PathOrKey}");
            }

            if (dryRun)
            {
                return Success(artifact, $"Dry-run: would delete key '{artifact.PathOrKey}'.");
            }

            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return Success(artifact, $"Deleted key '{artifact.PathOrKey}'.");
        }
        catch (Exception ex)
        {
            return Failure(artifact, $"Failed to delete key '{artifact.PathOrKey}': {ex.Message}");
        }
    }

    private ArtifactExecutionResult ExecuteStartupRegistryValueArtifact(UninstallArtifact artifact, bool dryRun)
    {
        var split = artifact.PathOrKey.Split("::", 2, StringSplitOptions.TrimEntries);
        if (split.Length != 2)
        {
            return Failure(artifact, $"Invalid startup registry value target: {artifact.PathOrKey}");
        }

        if (!TryParseRegistryPath(split[0], out var hive, out var subKey))
        {
            return Failure(artifact, $"Invalid startup registry path: {split[0]}");
        }

        var valueName = split[1];
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key is null)
            {
                return Failure(artifact, $"Startup key not found: {split[0]}");
            }

            if (key.GetValue(valueName) is null)
            {
                return Failure(artifact, $"Startup value not found: {artifact.PathOrKey}");
            }

            if (dryRun)
            {
                return Success(artifact, $"Dry-run: would delete startup value '{artifact.PathOrKey}'.");
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return Success(artifact, $"Deleted startup value '{artifact.PathOrKey}'.");
        }
        catch (Exception ex)
        {
            return Failure(artifact, $"Failed to delete startup value '{artifact.PathOrKey}': {ex.Message}");
        }
    }

    private async Task<ArtifactExecutionResult> ExecuteScheduledTaskArtifactAsync(
        UninstallArtifact artifact,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return Success(artifact, $"Dry-run: would delete scheduled task '{artifact.PathOrKey}'.");
        }

        var escapedTaskName = artifact.PathOrKey.Replace("\"", "\"\"");
        var result = await _commandRunner
            .RunAsync("schtasks.exe", $"/Delete /TN \"{escapedTaskName}\" /F", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0
            ? Success(artifact, $"Deleted scheduled task '{artifact.PathOrKey}'.")
            : Failure(artifact, $"Failed to delete scheduled task '{artifact.PathOrKey}': {result.StdErr}".Trim());
    }

    private async Task<ArtifactExecutionResult> ExecuteServiceArtifactAsync(
        UninstallArtifact artifact,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return Success(artifact, $"Dry-run: would stop and delete service '{artifact.PathOrKey}'.");
        }

        try
        {
            using var service = new ServiceController(artifact.PathOrKey);
            if (service.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
        }
        catch
        {
            // Continue to delete attempt, service may already be absent or inaccessible.
        }

        var result = await _commandRunner
            .RunAsync("sc.exe", $"delete \"{artifact.PathOrKey}\"", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0
            ? Success(artifact, $"Deleted service '{artifact.PathOrKey}'.")
            : Failure(artifact, $"Failed to delete service '{artifact.PathOrKey}': {result.StdErr}".Trim());
    }

    private ArtifactExecutionResult ExecuteProcessArtifact(UninstallArtifact artifact, bool dryRun)
    {
        var processName = Path.GetFileNameWithoutExtension(artifact.PathOrKey);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Failure(artifact, $"Invalid process name: {artifact.PathOrKey}");
        }

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            return Failure(artifact, $"No running process found for '{processName}'.");
        }

        if (dryRun)
        {
            return Success(artifact, $"Dry-run: would terminate {processes.Length} process(es) named '{processName}'.");
        }

        try
        {
            foreach (var process in processes)
            {
                process.Kill(entireProcessTree: true);
            }

            return Success(artifact, $"Terminated {processes.Length} process(es) named '{processName}'.");
        }
        catch (Exception ex)
        {
            return Failure(artifact, $"Failed to terminate process '{processName}': {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> TryCreateRestorePointAsync(CancellationToken cancellationToken)
    {
        var description = "Membran MultiTool cleanup";
        var command = $"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'";
        var result = await _commandRunner
            .RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"", 60_000, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0
            ? (true, "Restore point created.")
            : (false, $"Restore point creation failed (non-fatal): {result.StdErr}".Trim());
    }

    private static bool TryParseRegistryPath(string path, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.CurrentUser;
        subKey = string.Empty;

        var split = path.Split('\\', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        if (split[0] is not ("HKCU" or "HKEY_CURRENT_USER" or "HKLM" or "HKEY_LOCAL_MACHINE"))
        {
            return false;
        }

        subKey = split[1];
        return !string.IsNullOrWhiteSpace(subKey);
    }

    private bool IsAdminOnlyArtifact(UninstallArtifact artifact)
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

    private static ExecutionReport BuildReport(
        string planId,
        DateTimeOffset startedAt,
        List<ArtifactExecutionResult> artifactResults,
        List<string> failures,
        List<string> skipped)
    {
        return new ExecutionReport
        {
            PlanId = planId,
            RemovedCount = artifactResults.Count(result => result.Removed),
            FailedCount = artifactResults.Count(result => !result.Removed && !result.Skipped),
            SkippedCount = artifactResults.Count(result => result.Skipped),
            Failures = failures.Distinct().ToArray(),
            Skipped = skipped.Distinct().ToArray(),
            ArtifactResults = artifactResults.ToArray(),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private static ArtifactExecutionResult Success(UninstallArtifact artifact, string message)
    {
        return new ArtifactExecutionResult
        {
            ArtifactId = artifact.ArtifactId,
            Type = artifact.Type,
            Target = artifact.PathOrKey,
            Removed = true,
            Skipped = false,
            Message = message
        };
    }

    private static ArtifactExecutionResult Failure(UninstallArtifact artifact, string message)
    {
        return new ArtifactExecutionResult
        {
            ArtifactId = artifact.ArtifactId,
            Type = artifact.Type,
            Target = artifact.PathOrKey,
            Removed = false,
            Skipped = false,
            Message = message
        };
    }

    private static ArtifactExecutionResult CreateSkippedResult(string artifactId, string type, string target, string message)
    {
        return new ArtifactExecutionResult
        {
            ArtifactId = artifactId,
            Type = type,
            Target = target,
            Removed = false,
            Skipped = true,
            Message = message
        };
    }
}
