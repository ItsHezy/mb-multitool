using System.Net;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Windows;

namespace Membran.MultiTool.Osint.Services;

public sealed class OsintScanService(
    ToolRegistry toolRegistry,
    IToolDetector toolDetector,
    IToolRunner toolRunner,
    PhoneParserService phoneParserService,
    SystemCommandRunner? commandRunner = null)
{
    private readonly ToolRegistry _toolRegistry = toolRegistry;
    private readonly IToolDetector _toolDetector = toolDetector;
    private readonly IToolRunner _toolRunner = toolRunner;
    private readonly PhoneParserService _phoneParserService = phoneParserService;
    private readonly SystemCommandRunner _commandRunner = commandRunner ?? new SystemCommandRunner();

    public Task<IReadOnlyList<ToolAvailability>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        return _toolDetector.DetectAllAsync(cancellationToken);
    }

    public async Task<OsintToolsDoctorResult> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _toolDetector.DetectAllAsync(cancellationToken).ConfigureAwait(false);

        var dnsChecks = new List<DoctorCheck>
        {
            await ResolveHostCheckAsync("github.com").ConfigureAwait(false),
            await ResolveHostCheckAsync("pypi.org").ConfigureAwait(false)
        };

        return new OsintToolsDoctorResult
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Tools = tools.ToArray(),
            Checks = dnsChecks.ToArray()
        };
    }

    public IReadOnlyList<string> GetInstallHints(string? toolName = null)
    {
        if (string.IsNullOrWhiteSpace(toolName) || toolName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return _toolRegistry.ListDefinitions()
                .Select(item => $"{item.Name}: {item.InstallHint}")
                .ToArray();
        }

        if (!_toolRegistry.TryGetDefinition(toolName, out var definition))
        {
            return [$"Unknown tool '{toolName}'. Available: {string.Join(", ", _toolRegistry.ListToolNames())}"];
        }

        return [$"{definition.Name}: {definition.InstallHint}"];
    }

    public async Task<OsintUpdateReport> UpdateToolsAsync(
        string[] requestedTools,
        int timeoutSec,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var steps = new List<OsintUpdateStepResult>();

        var definitions = ResolveRequestedDefinitions(requestedTools, warnings);
        var hasPipTools = definitions.Any(item => !string.IsNullOrWhiteSpace(item.PipPackage));
        string? pythonLauncher = null;

        if (hasPipTools)
        {
            pythonLauncher = await ResolvePythonLauncherAsync(timeoutSec, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pythonLauncher))
            {
                warnings.Add("Python launcher not found. Skipping pip-based installs.");
            }
            else
            {
                steps.Add(await ExecuteStepAsync(
                        "global",
                        "pip-upgrade",
                        pythonLauncher,
                        "-m pip install --upgrade pip",
                        timeoutSec,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        var wingetReady = await IsWingetAvailableAsync(timeoutSec, cancellationToken).ConfigureAwait(false);
        if (!wingetReady)
        {
            warnings.Add("winget not available. Skipping winget-based installers.");
        }

        foreach (var definition in definitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.PipPackage))
            {
                if (string.IsNullOrWhiteSpace(pythonLauncher))
                {
                    steps.Add(new OsintUpdateStepResult
                    {
                        ToolName = definition.Name,
                        Step = "pip-install",
                        Command = $"python -m pip install --upgrade {definition.PipPackage}",
                        Succeeded = false,
                        ExitCode = -1,
                        Message = "Skipped: Python launcher not found."
                    });
                }
                else
                {
                    steps.Add(await ExecuteStepAsync(
                            definition.Name,
                            "pip-install",
                            pythonLauncher,
                            $"-m pip install --upgrade {definition.PipPackage}",
                            timeoutSec,
                            cancellationToken)
                        .ConfigureAwait(false));
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.WingetId))
            {
                if (!wingetReady)
                {
                    steps.Add(new OsintUpdateStepResult
                    {
                        ToolName = definition.Name,
                        Step = "winget-install",
                        Command = $"winget install --id {definition.WingetId} -e --accept-source-agreements --accept-package-agreements --silent",
                        Succeeded = false,
                        ExitCode = -1,
                        Message = "Skipped: winget not available."
                    });
                }
                else
                {
                    steps.Add(await ExecuteStepAsync(
                            definition.Name,
                            "winget-install",
                            "winget",
                            $"install --id {definition.WingetId} -e --accept-source-agreements --accept-package-agreements --silent",
                            timeoutSec,
                            cancellationToken)
                        .ConfigureAwait(false));
                }
            }

            if (string.IsNullOrWhiteSpace(definition.PipPackage) && string.IsNullOrWhiteSpace(definition.WingetId))
            {
                steps.Add(new OsintUpdateStepResult
                {
                    ToolName = definition.Name,
                    Step = "manual",
                    Command = definition.InstallHint,
                    Succeeded = true,
                    ExitCode = 0,
                    Message = "No automated installer configured; see install hint."
                });
            }
        }

        var postUpdateTools = await _toolDetector.DetectAllAsync(cancellationToken).ConfigureAwait(false);
        var postUpdateLookup = postUpdateTools
            .ToDictionary(item => item.ToolName, StringComparer.OrdinalIgnoreCase);

        foreach (var failedStep in steps.Where(item => !item.Succeeded && item.Step != "manual"))
        {
            if (!failedStep.ToolName.Equals("global", StringComparison.OrdinalIgnoreCase) &&
                postUpdateLookup.TryGetValue(failedStep.ToolName, out var detectedAfterFailure) &&
                detectedAfterFailure.Detected)
            {
                continue;
            }

            warnings.Add($"Step failed for '{failedStep.ToolName}' ({failedStep.Step}): {failedStep.Message}");
        }

        foreach (var definition in definitions)
        {
            var hasAutomatedInstaller =
                !string.IsNullOrWhiteSpace(definition.PipPackage) ||
                !string.IsNullOrWhiteSpace(definition.WingetId);

            if (!hasAutomatedInstaller)
            {
                continue;
            }

            if (!postUpdateLookup.TryGetValue(definition.Name, out var availability) || !availability.Detected)
            {
                warnings.Add($"Tool '{definition.Name}' is still not detected after update. {definition.InstallHint}");
            }
        }

        return new OsintUpdateReport
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            RequestedTools = definitions.Select(item => item.Name).ToArray(),
            Steps = steps.ToArray(),
            Tools = postUpdateTools.ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public async Task<ToolExecutionResult> RunToolAsync(
        string toolName,
        OsintTargetType targetType,
        string targetValue,
        bool consent,
        int timeoutSec,
        bool includeRaw,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent(consent);
        ValidateTarget(targetType, targetValue);

        var tools = await _toolDetector.DetectAllAsync(cancellationToken).ConfigureAwait(false);
        var availability = tools.FirstOrDefault(item => item.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (availability is null)
        {
            return new ToolExecutionResult
            {
                ToolName = toolName,
                Executed = false,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = $"Tool '{toolName}' is not supported by this build."
            };
        }

        return await _toolRunner
            .RunAsync(availability, targetType, targetValue, timeoutSec, includeRaw, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OsintScanOutcome> ScanAsync(
        OsintScanRequest request,
        bool runAllFoundDefault,
        int maxParallelTools,
        bool includeRaw,
        CancellationToken cancellationToken = default)
    {
        EnsureConsent(request.ConsentAcknowledged);
        ValidateTarget(request.TargetType, request.TargetValue);

        var startedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var requestedUnavailableTools = false;
        var allTools = await _toolDetector.DetectAllAsync(cancellationToken).ConfigureAwait(false);

        var toolLookup = allTools.ToDictionary(item => item.ToolName, StringComparer.OrdinalIgnoreCase);
        var compatibleTools = allTools
            .Where(item => item.CapabilityTargets.Contains(request.TargetType))
            .ToArray();

        var selectedNames = BuildSelectedTools(
            request.RequestedTools,
            compatibleTools,
            runAllFoundDefault);

        var executionResults = new List<ToolExecutionResult>();
        var tasks = new List<Task<ToolExecutionResult>>();
        var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelTools), Math.Max(1, maxParallelTools));

        foreach (var toolName in selectedNames)
        {
            if (!toolLookup.TryGetValue(toolName, out var availability))
            {
                requestedUnavailableTools = requestedUnavailableTools || request.RequestedTools.Length > 0;
                warnings.Add($"Requested tool '{toolName}' is unknown.");
                executionResults.Add(new ToolExecutionResult
                {
                    ToolName = toolName,
                    Executed = false,
                    Succeeded = false,
                    ExitCode = -1,
                    ParsedSummary = $"Unknown tool '{toolName}'."
                });
                continue;
            }

            if (!availability.CapabilityTargets.Contains(request.TargetType))
            {
                requestedUnavailableTools = requestedUnavailableTools || request.RequestedTools.Length > 0;
                warnings.Add($"Tool '{toolName}' does not support target type '{request.TargetType}'.");
                executionResults.Add(new ToolExecutionResult
                {
                    ToolName = toolName,
                    Executed = false,
                    Succeeded = false,
                    ExitCode = -1,
                    ParsedSummary = $"Incompatible target type '{request.TargetType}'."
                });
                continue;
            }

            if (!availability.Detected && request.RequestedTools.Length > 0)
            {
                requestedUnavailableTools = true;
            }

            tasks.Add(RunToolWithSemaphoreAsync(
                semaphore,
                availability,
                request.TargetType,
                request.TargetValue,
                request.TimeoutSec,
                includeRaw,
                cancellationToken));
        }

        if (tasks.Count > 0)
        {
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            executionResults.AddRange(results);
        }

        if (selectedNames.Length == 0)
        {
            warnings.Add("No tools selected for this scan.");
        }
        else if (executionResults.All(item => !item.Executed && !item.Succeeded))
        {
            warnings.Add("No compatible detected tools executed. Use 'mb osint tools list' and 'install-hints'.");
        }

        PhoneParseResult? phoneParse = null;
        if (request.TargetType == OsintTargetType.Phone)
        {
            phoneParse = _phoneParserService.Parse(request.TargetValue);
            if (!string.IsNullOrWhiteSpace(phoneParse.Error))
            {
                warnings.Add(phoneParse.Error);
            }
        }

        var aggregateFindings = BuildAggregateFindings(phoneParse, executionResults);
        var report = new OsintScanReport
        {
            ScanId = request.ScanId,
            TargetType = request.TargetType,
            TargetValue = request.TargetValue,
            ConsentAcknowledgedAt = DateTimeOffset.UtcNow,
            RemovedSensitiveInText = !includeRaw,
            PhoneParse = phoneParse,
            ToolResults = executionResults.ToArray(),
            AggregateFindings = aggregateFindings,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };

        return new OsintScanOutcome
        {
            Report = report,
            RequestedUnavailableTools = requestedUnavailableTools
        };
    }

    private async Task<ToolExecutionResult> RunToolWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        ToolAvailability availability,
        OsintTargetType targetType,
        string targetValue,
        int timeoutSec,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _toolRunner
                .RunAsync(availability, targetType, targetValue, timeoutSec, includeRaw, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = true,
                Succeeded = false,
                ExitCode = -1,
                ParsedSummary = $"Execution failed: {ex.Message}"
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string[] BuildSelectedTools(
        string[] requestedTools,
        IReadOnlyList<ToolAvailability> compatibleTools,
        bool runAllFoundDefault)
    {
        if (requestedTools.Length > 0)
        {
            return requestedTools
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!runAllFoundDefault)
        {
            return Array.Empty<string>();
        }

        return compatibleTools
            .Where(item => item.Detected)
            .Select(item => item.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OsintFinding[] BuildAggregateFindings(
        PhoneParseResult? phoneParse,
        IEnumerable<ToolExecutionResult> toolResults)
    {
        var findings = new List<OsintFinding>();

        if (phoneParse is not null && string.IsNullOrWhiteSpace(phoneParse.Error))
        {
            findings.Add(new OsintFinding
            {
                Source = "phone-parser",
                Type = "phone_e164",
                Indicator = phoneParse.E164,
                Confidence = "high",
                Context = "Deterministic normalization."
            });

            if (!string.IsNullOrWhiteSpace(phoneParse.RegionCode))
            {
                findings.Add(new OsintFinding
                {
                    Source = "phone-parser",
                    Type = "region",
                    Indicator = phoneParse.RegionCode,
                    Confidence = "high",
                    Context = "Region from numbering plan metadata."
                });
            }

            if (!string.IsNullOrWhiteSpace(phoneParse.NumberType))
            {
                findings.Add(new OsintFinding
                {
                    Source = "phone-parser",
                    Type = "number_type",
                    Indicator = phoneParse.NumberType,
                    Confidence = "high",
                    Context = "Number type from numbering plan metadata."
                });
            }

            foreach (var zone in phoneParse.TimeZones.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                findings.Add(new OsintFinding
                {
                    Source = "phone-parser",
                    Type = "timezone",
                    Indicator = zone,
                    Confidence = "medium",
                    Context = "Time zone mapping metadata."
                });
            }
        }

        findings.AddRange(toolResults.SelectMany(item => item.Findings));

        return findings
            .GroupBy(item => $"{item.Type}|{item.Indicator}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var confidence = group.Count() > 1 && first.Confidence == "low"
                    ? "medium"
                    : first.Confidence;
                return new OsintFinding
                {
                    Source = first.Source,
                    Type = first.Type,
                    Indicator = first.Indicator,
                    Confidence = confidence,
                    Context = first.Context
                };
            })
            .ToArray();
    }

    private static void EnsureConsent(bool consent)
    {
        if (!consent)
        {
            throw new InvalidOperationException("OSINT scanning requires --consent. Use only for lawful/publicly authorized investigations.");
        }
    }

    private static void ValidateTarget(OsintTargetType targetType, string targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new ArgumentException("Target value is required.");
        }

        if (targetType == OsintTargetType.Ip && !IPAddress.TryParse(targetValue, out _))
        {
            throw new ArgumentException("Target value is not a valid IP address.");
        }
    }

    private static async Task<DoctorCheck> ResolveHostCheckAsync(string host)
    {
        try
        {
            var lookupTask = Dns.GetHostAddressesAsync(host);
            var completed = await Task.WhenAny(lookupTask, Task.Delay(3_000)).ConfigureAwait(false);
            if (completed != lookupTask)
            {
                return new DoctorCheck(host, "warn", "DNS lookup timed out.");
            }

            var addresses = await lookupTask.ConfigureAwait(false);
            return addresses.Length > 0
                ? new DoctorCheck(host, "pass", "DNS resolution succeeded.")
                : new DoctorCheck(host, "warn", "DNS resolution returned no addresses.");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(host, "warn", $"DNS resolution failed: {ex.Message}");
        }
    }

    private IReadOnlyList<ToolDefinition> ResolveRequestedDefinitions(string[] requestedTools, List<string> warnings)
    {
        if (requestedTools.Length == 0 ||
            requestedTools.Any(item => item.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            return _toolRegistry.ListDefinitions();
        }

        var definitions = new List<ToolDefinition>();
        foreach (var requested in requestedTools
                     .SelectMany(item => item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_toolRegistry.TryGetDefinition(requested, out var definition))
            {
                definitions.Add(definition);
            }
            else
            {
                warnings.Add($"Unknown tool '{requested}'.");
            }
        }

        return definitions;
    }

    private async Task<string?> ResolvePythonLauncherAsync(int timeoutSec, CancellationToken cancellationToken)
    {
        var py = await _commandRunner
            .RunAsync("py", "-m pip --version", timeoutSec * 1_000, cancellationToken)
            .ConfigureAwait(false);
        if (py.ExitCode == 0)
        {
            return "py";
        }

        var python = await _commandRunner
            .RunAsync("python", "-m pip --version", timeoutSec * 1_000, cancellationToken)
            .ConfigureAwait(false);
        if (python.ExitCode == 0)
        {
            return "python";
        }

        return null;
    }

    private async Task<bool> IsWingetAvailableAsync(int timeoutSec, CancellationToken cancellationToken)
    {
        var result = await _commandRunner
            .RunAsync("winget", "--version", timeoutSec * 1_000, cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private async Task<OsintUpdateStepResult> ExecuteStepAsync(
        string toolName,
        string step,
        string fileName,
        string arguments,
        int timeoutSec,
        CancellationToken cancellationToken)
    {
        var result = await _commandRunner
            .RunAsync(fileName, arguments, timeoutSec * 1_000, cancellationToken)
            .ConfigureAwait(false);

        var output = string.Join(Environment.NewLine, result.StdOut, result.StdErr).Trim();
        var firstLine = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var message = firstLine ?? (result.ExitCode == 0 ? "ok" : $"failed (exit code {result.ExitCode})");

        return new OsintUpdateStepResult
        {
            ToolName = toolName,
            Step = step,
            Command = $"{fileName} {arguments}",
            Succeeded = result.ExitCode == 0,
            ExitCode = result.ExitCode,
            Message = message
        };
    }
}

public sealed class OsintScanOutcome
{
    public OsintScanReport Report { get; init; } = new();
    public bool RequestedUnavailableTools { get; init; }
}

public sealed class OsintToolsDoctorResult
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public ToolAvailability[] Tools { get; init; } = Array.Empty<ToolAvailability>();
    public DoctorCheck[] Checks { get; init; } = Array.Empty<DoctorCheck>();
}

public sealed class DoctorCheck
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

public sealed class OsintUpdateReport
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public string[] RequestedTools { get; init; } = Array.Empty<string>();
    public OsintUpdateStepResult[] Steps { get; init; } = Array.Empty<OsintUpdateStepResult>();
    public ToolAvailability[] Tools { get; init; } = Array.Empty<ToolAvailability>();
    public string[] Warnings { get; init; } = Array.Empty<string>();
}

public sealed class OsintUpdateStepResult
{
    public string ToolName { get; init; } = string.Empty;
    public string Step { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
}
