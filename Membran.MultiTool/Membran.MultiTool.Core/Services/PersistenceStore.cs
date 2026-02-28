using System.Text.Json;
using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Core.Services;

public sealed class PersistenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _plansDirectory;
    private readonly string _reportsDirectory;
    private readonly string _osintReportsDirectory;

    public PersistenceStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? ResolveDefaultRootDirectory();
        _plansDirectory = Path.Combine(RootDirectory, "plans");
        _reportsDirectory = Path.Combine(RootDirectory, "reports");
        _osintReportsDirectory = Path.Combine(RootDirectory, "osint-reports");

        Directory.CreateDirectory(_plansDirectory);
        Directory.CreateDirectory(_reportsDirectory);
        Directory.CreateDirectory(_osintReportsDirectory);
    }

    public string RootDirectory { get; }

    public void SavePlan(UninstallPlan plan)
    {
        var path = Path.Combine(_plansDirectory, $"{plan.PlanId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public bool TryGetPlan(string planId, out UninstallPlan? plan)
    {
        var path = Path.Combine(_plansDirectory, $"{planId}.json");
        if (!File.Exists(path))
        {
            plan = null;
            return false;
        }

        plan = JsonSerializer.Deserialize<UninstallPlan>(File.ReadAllText(path), JsonOptions);
        return plan is not null;
    }

    public IEnumerable<UninstallPlan> ListPlans()
    {
        foreach (var file in Directory.EnumerateFiles(_plansDirectory, "*.json"))
        {
            UninstallPlan? plan = null;
            try
            {
                plan = JsonSerializer.Deserialize<UninstallPlan>(File.ReadAllText(file), JsonOptions);
            }
            catch
            {
                // Ignore malformed plan files.
            }

            if (plan is not null)
            {
                yield return plan;
            }
        }
    }

    public void SaveReport(ExecutionReport report)
    {
        var fileName = $"{report.PlanId}-{report.CompletedAt:yyyyMMddHHmmss}.json";
        var path = Path.Combine(_reportsDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }

    public IEnumerable<ExecutionReport> ListReports()
    {
        foreach (var file in Directory.EnumerateFiles(_reportsDirectory, "*.json"))
        {
            ExecutionReport? report = null;
            try
            {
                report = JsonSerializer.Deserialize<ExecutionReport>(File.ReadAllText(file), JsonOptions);
            }
            catch
            {
                // Ignore malformed report files.
            }

            if (report is not null)
            {
                yield return report;
            }
        }
    }

    public bool TryGetLatestReport(string planId, out ExecutionReport? report)
    {
        report = ListReports()
            .Where(item => item.PlanId.Equals(planId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .FirstOrDefault();

        return report is not null;
    }

    public void SaveOsintReport(OsintScanReport report)
    {
        var fileName = $"{report.ScanId}-{report.CompletedAt:yyyyMMddHHmmss}.json";
        var path = Path.Combine(_osintReportsDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }

    public IEnumerable<OsintScanReport> ListOsintReports()
    {
        foreach (var file in Directory.EnumerateFiles(_osintReportsDirectory, "*.json"))
        {
            OsintScanReport? report = null;
            try
            {
                report = JsonSerializer.Deserialize<OsintScanReport>(File.ReadAllText(file), JsonOptions);
            }
            catch
            {
                // Ignore malformed report files.
            }

            if (report is not null)
            {
                yield return report;
            }
        }
    }

    public bool TryGetOsintReport(string scanId, out OsintScanReport? report)
    {
        report = ListOsintReports()
            .Where(item => item.ScanId.Equals(scanId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .FirstOrDefault();

        return report is not null;
    }

    public static string ResolveDefaultRootDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programData, "Membran.MultiTool"),
            Path.Combine(localData, "Membran.MultiTool")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch
            {
                // Try next candidate.
            }
        }

        var fallback = Path.Combine(Path.GetTempPath(), "Membran.MultiTool");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
