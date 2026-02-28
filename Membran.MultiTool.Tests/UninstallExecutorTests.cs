using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Windows;
using Membran.MultiTool.Uninstall.Services;
using System.Runtime.Versioning;

namespace Membran.MultiTool.Tests;

[SupportedOSPlatform("windows")]
public sealed class UninstallExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_StrictNonAdminMode_ReturnsAdminFailure()
    {
        var executor = new UninstallExecutor(
            new PathGuard(),
            new SystemCommandRunner(),
            isElevatedEvaluator: () => false);

        var plan = new UninstallPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            AppId = "app-strict",
            DisplayName = "StrictApp",
            RequiresAdmin = true,
            Artifacts =
            [
                new UninstallArtifact
                {
                    ArtifactId = "a1",
                    Type = "registry-key",
                    PathOrKey = @"HKLM\Software\StrictApp",
                    Evidence = "test",
                    RiskLevel = "high"
                }
            ]
        };

        var report = await executor.ExecuteAsync(plan, new UninstallExecutionOptions
        {
            Confirm = true,
            DryRun = false,
            AttemptRestorePoint = false,
            AllowPartialWithoutAdmin = false
        });

        Assert.Equal(0, report.RemovedCount);
        Assert.Equal(0, report.SkippedCount);
        Assert.Empty(report.ArtifactResults);
        Assert.Contains("administrator", string.Join(' ', report.Failures), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_PartialNonAdminMode_SkipsPrivilegedAndRunsUserSafe()
    {
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Membran.MultiTool.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(userPath);

        try
        {
            var executor = new UninstallExecutor(
                new PathGuard(),
                new SystemCommandRunner(),
                isElevatedEvaluator: () => false);

            var plan = new UninstallPlan
            {
                PlanId = Guid.NewGuid().ToString("N"),
                AppId = "app-partial",
                DisplayName = "PartialApp",
                RequiresAdmin = true,
                RestorePointPlanned = true,
                Artifacts =
                [
                    new UninstallArtifact
                    {
                        ArtifactId = "f1",
                        Type = "file",
                        PathOrKey = userPath,
                        Evidence = "test-user-safe",
                        RiskLevel = "medium"
                    },
                    new UninstallArtifact
                    {
                        ArtifactId = "r1",
                        Type = "registry-key",
                        PathOrKey = @"HKLM\Software\PartialApp",
                        Evidence = "test-admin-only",
                        RiskLevel = "high"
                    }
                ]
            };

            var report = await executor.ExecuteAsync(plan, new UninstallExecutionOptions
            {
                Confirm = true,
                DryRun = false,
                AttemptRestorePoint = true,
                AllowPartialWithoutAdmin = true
            });

            Assert.True(report.RemovedCount >= 1);
            Assert.True(report.SkippedCount >= 2); // restore point + HKLM registry key
            Assert.Contains(report.ArtifactResults, r => r.Type == "file" && r.Removed);
            Assert.Contains(report.ArtifactResults, r => r.Type == "registry-key" && r.Skipped);
            Assert.True(report.Skipped.Length >= 2);
        }
        finally
        {
            if (Directory.Exists(userPath))
            {
                Directory.Delete(userPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_PartialModeWithAdmin_DoesNotSkipAdminOnlyArtifacts()
    {
        var executor = new UninstallExecutor(
            new PathGuard(),
            new SystemCommandRunner(),
            isElevatedEvaluator: () => true);

        var plan = new UninstallPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            AppId = "app-admin",
            DisplayName = "AdminApp",
            RequiresAdmin = true,
            Artifacts =
            [
                new UninstallArtifact
                {
                    ArtifactId = "s1",
                    Type = "service",
                    PathOrKey = "FakeServiceForDryRun",
                    Evidence = "admin-only",
                    RiskLevel = "high"
                }
            ]
        };

        var report = await executor.ExecuteAsync(plan, new UninstallExecutionOptions
        {
            Confirm = true,
            DryRun = true,
            AttemptRestorePoint = false,
            AllowPartialWithoutAdmin = true
        });

        Assert.Equal(0, report.SkippedCount);
        Assert.Contains(report.ArtifactResults, r => r.Type == "service" && !r.Skipped && r.Removed);
    }
}
