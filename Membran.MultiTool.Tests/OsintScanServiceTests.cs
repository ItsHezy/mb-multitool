using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Osint.Services;

namespace Membran.MultiTool.Tests;

public sealed class OsintScanServiceTests
{
    [Fact]
    public async Task ScanAsync_Throws_WhenConsentMissing()
    {
        var service = CreateService(
            detectedTools: [],
            runner: new FakeToolRunner());

        var request = new OsintScanRequest
        {
            TargetType = OsintTargetType.Phone,
            TargetValue = "+14155552671",
            ConsentAcknowledged = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ScanAsync(request, runAllFoundDefault: true, maxParallelTools: 2, includeRaw: false));
    }

    [Fact]
    public async Task ScanAsync_SetsRequestedUnavailableTools_WhenExplicitToolMissing()
    {
        var service = CreateService(
            detectedTools:
            [
                new ToolAvailability
                {
                    ToolName = "phoneinfoga",
                    Detected = false,
                    CapabilityTargets = [OsintTargetType.Phone]
                }
            ],
            runner: new FakeToolRunner());

        var request = new OsintScanRequest
        {
            TargetType = OsintTargetType.Phone,
            TargetValue = "+14155552671",
            ConsentAcknowledged = true,
            RequestedTools = ["phoneinfoga"]
        };

        var outcome = await service.ScanAsync(
            request,
            runAllFoundDefault: true,
            maxParallelTools: 2,
            includeRaw: false);

        Assert.True(outcome.RequestedUnavailableTools);
        Assert.Single(outcome.Report.ToolResults);
        Assert.False(outcome.Report.ToolResults[0].Succeeded);
    }

    [Fact]
    public async Task ScanAsync_RunAllFoundDefault_ExecutesDetectedCompatibleTools()
    {
        var runner = new FakeToolRunner();
        var service = CreateService(
            detectedTools:
            [
                new ToolAvailability
                {
                    ToolName = "phoneinfoga",
                    Detected = true,
                    CapabilityTargets = [OsintTargetType.Phone]
                },
                new ToolAvailability
                {
                    ToolName = "sherlock",
                    Detected = true,
                    CapabilityTargets = [OsintTargetType.Username]
                }
            ],
            runner: runner);

        var request = new OsintScanRequest
        {
            TargetType = OsintTargetType.Phone,
            TargetValue = "+14155552671",
            ConsentAcknowledged = true
        };

        var outcome = await service.ScanAsync(
            request,
            runAllFoundDefault: true,
            maxParallelTools: 2,
            includeRaw: false);

        Assert.False(outcome.RequestedUnavailableTools);
        Assert.Single(outcome.Report.ToolResults);
        Assert.Single(runner.Calls);
        Assert.Equal("phoneinfoga", runner.Calls[0]);
    }

    private static OsintScanService CreateService(IReadOnlyList<ToolAvailability> detectedTools, IToolRunner runner)
    {
        var registry = new ToolRegistry();
        return new OsintScanService(
            registry,
            new FakeToolDetector(detectedTools),
            runner,
            new PhoneParserService());
    }

    private sealed class FakeToolDetector(IReadOnlyList<ToolAvailability> tools) : IToolDetector
    {
        private readonly IReadOnlyList<ToolAvailability> _tools = tools;

        public Task<IReadOnlyList<ToolAvailability>> DetectAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_tools);
        }
    }

    private sealed class FakeToolRunner : IToolRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ToolExecutionResult> RunAsync(
            ToolAvailability availability,
            OsintTargetType targetType,
            string targetValue,
            int timeoutSec,
            bool includeRaw,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(availability.ToolName);
            if (!availability.Detected)
            {
                return Task.FromResult(new ToolExecutionResult
                {
                    ToolName = availability.ToolName,
                    Executed = false,
                    Succeeded = false,
                    ExitCode = -1,
                    ParsedSummary = "Tool not detected."
                });
            }

            return Task.FromResult(new ToolExecutionResult
            {
                ToolName = availability.ToolName,
                Executed = true,
                Succeeded = true,
                ExitCode = 0,
                ParsedSummary = "ok",
                Findings =
                [
                    new OsintFinding
                    {
                        Source = availability.ToolName,
                        Type = "text",
                        Indicator = "example",
                        Confidence = "low",
                        Context = "test"
                    }
                ]
            });
        }
    }
}
