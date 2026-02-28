using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Geo.Providers;
using Membran.MultiTool.Geo.Services;

namespace Membran.MultiTool.Tests;

public sealed class GeoLookupServiceTests
{
    [Fact]
    public void CalculateConfidence_AssignsHighScore_WhenMajorFieldsExist()
    {
        var result = new GeoResult
        {
            Country = "US",
            Region = "California",
            City = "San Francisco",
            Latitude = 37.77,
            Longitude = -122.42,
            Isp = "Example ISP"
        };

        var score = GeoLookupService.CalculateConfidence(result);

        Assert.InRange(score, 0.75, 0.95);
    }

    [Fact]
    public void CalculateConfidence_ReducesScore_ForProxySignals()
    {
        var baseResult = new GeoResult
        {
            Country = "US",
            Region = "California",
            City = "San Francisco",
            Latitude = 37.77,
            Longitude = -122.42,
            Isp = "Example ISP"
        };

        var proxyResult = new GeoResult
        {
            Country = baseResult.Country,
            Region = baseResult.Region,
            City = baseResult.City,
            Latitude = baseResult.Latitude,
            Longitude = baseResult.Longitude,
            Isp = "Some VPN Proxy Service"
        };

        var baseline = GeoLookupService.CalculateConfidence(baseResult);
        var withProxy = GeoLookupService.CalculateConfidence(proxyResult);

        Assert.True(withProxy < baseline);
    }

    [Fact]
    public async Task LookupAsync_RejectsReservedExampleIp()
    {
        var service = new GeoLookupService(
        [
            new NoopProvider()
        ]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.LookupAsync(new GeoQuery { InputIpOrSelf = "192.0.0.1" }));

        Assert.Contains("Private/reserved", ex.Message);
    }

    private sealed class NoopProvider : IGeoProvider
    {
        public string Name => "noop";

        public Task<GeoResult?> LookupAsync(string ipOrSelf, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GeoResult?>(null);
        }
    }
}
