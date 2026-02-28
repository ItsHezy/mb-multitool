using System.Text.Json;
using System.Globalization;
using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Geo.Providers;

public sealed class IpApiGeoProvider(HttpClient httpClient) : IGeoProvider
{
    public string Name => "ipapi.co";

    public async Task<GeoResult?> LookupAsync(
        string ipOrSelf,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ipOrSelf.Equals("self", StringComparison.OrdinalIgnoreCase)
            ? "https://ipapi.co/json/"
            : $"https://ipapi.co/{Uri.EscapeDataString(ipOrSelf)}/json/";

        using var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return new GeoResult
        {
            QueryIp = ReadString(root, "ip"),
            Country = ReadString(root, "country_name"),
            Region = ReadString(root, "region"),
            City = ReadString(root, "city"),
            Latitude = ReadDouble(root, "latitude"),
            Longitude = ReadDouble(root, "longitude"),
            Isp = ReadString(root, "org"),
            SourceProvider = Name
        };
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return string.Empty;
        }

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : prop.ToString();
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
        {
            return value;
        }

        if (prop.ValueKind == JsonValueKind.String &&
            double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
