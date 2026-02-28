namespace Membran.MultiTool.Core.Models;

public sealed class GeoQuery
{
    public string InputIpOrSelf { get; init; } = "self";
}

public sealed class GeoResult
{
    public string QueryIp { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string Isp { get; init; } = string.Empty;
    public string SourceProvider { get; init; } = string.Empty;
    public string AccuracyLabel { get; init; } = "Approximate (IP-based)";
    public double Confidence { get; init; }
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;
}
