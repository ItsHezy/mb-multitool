using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Services;

namespace Membran.MultiTool.Tests;

public sealed class PersistenceStoreTests
{
    [Fact]
    public void SavePlan_ThenLoadPlan_RoundTrips()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new PersistenceStore(root);
            var plan = new UninstallPlan
            {
                PlanId = Guid.NewGuid().ToString("N"),
                AppId = "app-1",
                DisplayName = "Example App",
                Artifacts =
                [
                    new UninstallArtifact
                    {
                        ArtifactId = "a1",
                        Type = "file",
                        PathOrKey = @"C:\\Temp\\Example",
                        Evidence = "test",
                        RiskLevel = "low"
                    }
                ]
            };

            store.SavePlan(plan);
            var found = store.TryGetPlan(plan.PlanId, out var loaded);

            Assert.True(found);
            Assert.NotNull(loaded);
            Assert.Equal(plan.PlanId, loaded!.PlanId);
            Assert.Single(loaded.Artifacts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveReport_ThenGetLatestReport_ReturnsNewest()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new PersistenceStore(root);
            var planId = "plan-123";

            store.SaveReport(new ExecutionReport
            {
                PlanId = planId,
                RemovedCount = 1,
                FailedCount = 0,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

            store.SaveReport(new ExecutionReport
            {
                PlanId = planId,
                RemovedCount = 2,
                FailedCount = 1,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt = DateTimeOffset.UtcNow
            });

            var found = store.TryGetLatestReport(planId, out var latest);

            Assert.True(found);
            Assert.NotNull(latest);
            Assert.Equal(2, latest!.RemovedCount);
            Assert.Equal(1, latest.FailedCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveOsintReport_ThenGetByScanId_RoundTrips()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new PersistenceStore(root);
            var scanId = Guid.NewGuid().ToString("N");
            var report = new OsintScanReport
            {
                ScanId = scanId,
                TargetType = OsintTargetType.Phone,
                TargetValue = "+14155552671",
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
                CompletedAt = DateTimeOffset.UtcNow,
                ToolResults =
                [
                    new ToolExecutionResult
                    {
                        ToolName = "phoneinfoga",
                        Executed = true,
                        Succeeded = true,
                        ExitCode = 0,
                        ParsedSummary = "ok"
                    }
                ]
            };

            store.SaveOsintReport(report);
            var found = store.TryGetOsintReport(scanId, out var loaded);

            Assert.True(found);
            Assert.NotNull(loaded);
            Assert.Equal(scanId, loaded!.ScanId);
            Assert.Equal(OsintTargetType.Phone, loaded.TargetType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Membran.MultiTool.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
