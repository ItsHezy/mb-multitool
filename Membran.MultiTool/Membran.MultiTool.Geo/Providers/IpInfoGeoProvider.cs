using System.Text.Json;
using System.Globalization;
using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Geo.Providers;

public sealed class IpInfoGeoProvider(HttpClient httpClient) : IGeoProvider
{
    public string Name => "ipinfo.io";

    public async Task<GeoResult?> LookupAsync(
        string ipOrSelf,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ipOrSelf.Equals("self", StringComparison.OrdinalIgnoreCase)
            ? "https://ipinfo.io/json"
            : $"https://ipinfo.io/{Uri.EscapeDataString(ipOrSelf)}/json";

        using var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("bogon", out var bogonProp) && bogonProp.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        (double? latitude, double? longitude) = ParseCoordinates(ReadString(root, "loc"));

        return new GeoResult
        {
            QueryIp = ReadString(root, "ip"),
            Country = ReadString(root, "country"),
            Region = ReadString(root, "region"),
            City = ReadString(root, "city"),
            Latitude = latitude,
            Longitude = longitude,
            Isp = ReadString(root, "org"),
            SourceProvider = Name
        };
    }

    private static (double? Latitude, double? Longitude) ParseCoordinates(string loc)
    {
        if (string.IsNullOrWhiteSpace(loc))
        {
            return (null, null);
        }

        var split = loc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length != 2)
        {
            return (null, null);
        }

        if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return (null, null);
        }

        return (lat, lon);
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return string.Empty;
        }

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : prop.ToString();
    }
}
