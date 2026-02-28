using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Osint.Services;

namespace Membran.MultiTool.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public void ListDefinitions_ContainsAllPlannedTools()
    {
        var registry = new ToolRegistry();

        var names = registry.ListToolNames();

        Assert.Equal(10, names.Count);
        Assert.Contains("phoneinfoga", names);
        Assert.Contains("sherlock", names);
        Assert.Contains("osrframework", names);
    }

    [Fact]
    public void SupportsTarget_ReturnsExpectedCapabilities()
    {
        var registry = new ToolRegistry();

        Assert.True(registry.SupportsTarget("phoneinfoga", OsintTargetType.Phone));
        Assert.False(registry.SupportsTarget("phoneinfoga", OsintTargetType.Username));
        Assert.True(registry.SupportsTarget("maigret", OsintTargetType.Username));
    }

    [Fact]
    public void Definitions_ExposeExpectedInstallerMetadata()
    {
        var registry = new ToolRegistry();

        Assert.True(registry.TryGetDefinition("maltego", out var maltego));
        Assert.Equal("Maltego.Maltego", maltego.WingetId);

        Assert.True(registry.TryGetDefinition("phoneinfoga", out var phoneinfoga));
        Assert.True(string.IsNullOrWhiteSpace(phoneinfoga.WingetId));

        Assert.True(registry.TryGetDefinition("recon-ng", out var reconNg));
        Assert.True(string.IsNullOrWhiteSpace(reconNg.PipPackage));

        Assert.True(registry.TryGetDefinition("spiderfoot", out var spiderfoot));
        Assert.True(string.IsNullOrWhiteSpace(spiderfoot.PipPackage));

        Assert.True(registry.TryGetDefinition("theharvester", out var theHarvester));
        Assert.Equal("git+https://github.com/laramies/theHarvester.git", theHarvester.PipPackage);
    }
}
