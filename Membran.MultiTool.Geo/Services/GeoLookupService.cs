using System.Collections.Concurrent;
using System.Net;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Geo.Providers;

namespace Membran.MultiTool.Geo.Services;

public sealed class GeoLookupService(IEnumerable<IGeoProvider> providers)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private readonly IGeoProvider[] _providers = providers.ToArray();
    private readonly ConcurrentDictionary<string, (GeoResult Result, DateTimeOffset ExpiresAt)> _cache = new();

    public async Task<GeoResult> LookupAsync(GeoQuery query, CancellationToken cancellationToken = default)
    {
        var input = query.InputIpOrSelf.Trim();
        var cacheKey = input.ToLowerInvariant();
        ValidateInput(input);

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Result;
        }

        foreach (var provider in _providers)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var raw = await provider.LookupAsync(input, timeout.Token).ConfigureAwait(false);
                    if (raw is null)
                    {
                        continue;
                    }

                    var normalized = new GeoResult
                    {
                        QueryIp = string.IsNullOrWhiteSpace(raw.QueryIp) ? input : raw.QueryIp,
                        Country = raw.Country,
                        Region = raw.Region,
                        City = raw.City,
                        Latitude = raw.Latitude,
                        Longitude = raw.Longitude,
                        Isp = raw.Isp,
                        SourceProvider = provider.Name,
                        AccuracyLabel = "Approximate (IP-based)",
                        Confidence = CalculateConfidence(raw),
                        RetrievedAt = DateTimeOffset.UtcNow
                    };

                    _cache[cacheKey] = (normalized, DateTimeOffset.UtcNow.Add(CacheTtl));
                    return normalized;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Provider timeout, retry once.
                }
                catch
                {
                    // Provider error, continue with fallback.
                }
            }
        }

        throw new InvalidOperationException("No geolocation provider returned a valid response.");
    }

    public static double CalculateConfidence(GeoResult result)
    {
        double score = 0.2;

        if (!string.IsNullOrWhiteSpace(result.Country))
        {
            score += 0.25;
        }

        if (!string.IsNullOrWhiteSpace(result.Region))
        {
            score += 0.15;
        }

        if (!string.IsNullOrWhiteSpace(result.City))
        {
            score += 0.15;
        }

        if (result.Latitude.HasValue && result.Longitude.HasValue)
        {
            score += 0.15;
        }

        if (!string.IsNullOrWhiteSpace(result.Isp))
        {
            score += 0.1;
        }

        var isp = result.Isp.ToLowerInvariant();
        if (isp.Contains("proxy") || isp.Contains("vpn") || isp.Contains("tor"))
        {
            score -= 0.15;
        }

        return Math.Clamp(score, 0.1, 0.95);
    }

    private static void ValidateInput(string input)
    {
        if (input.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IPAddress.TryParse(input, out var ip))
        {
            throw new ArgumentException("Input must be 'self', IPv4, or IPv6.");
        }

        if (IsPrivateOrReserved(ip))
        {
            throw new ArgumentException("Private/reserved IP ranges cannot be geolocated reliably.");
        }
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) ||
            ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.None) ||
            ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];

            var isUniqueLocal = (first & 0xFE) == 0xFC; // fc00::/7
            var isLinkLocal = first == 0xFE && (second & 0xC0) == 0x80; // fe80::/10
            return isUniqueLocal || isLinkLocal;
        }

        var v4 = ip.GetAddressBytes();
        var b0 = v4[0];
        var b1 = v4[1];
        var b2 = v4[2];

        // RFC1918 private ranges.
        if (b0 == 10 ||
            (b0 == 172 && b1 >= 16 && b1 <= 31) ||
            (b0 == 192 && b1 == 168))
        {
            return true;
        }

        // Loopback, link-local, CGNAT.
        if (b0 == 127 ||
            (b0 == 169 && b1 == 254) ||
            (b0 == 100 && b1 >= 64 && b1 <= 127))
        {
            return true;
        }

        // Documentation/reserved examples.
        if ((b0 == 192 && b1 == 0 && b2 == 0) || // 192.0.0.0/24
            (b0 == 192 && b1 == 0 && b2 == 2) || // 192.0.2.0/24
            (b0 == 198 && b1 == 51 && b2 == 100) || // 198.51.100.0/24
            (b0 == 203 && b1 == 0 && b2 == 113)) // 203.0.113.0/24
        {
            return true;
        }

        return false;
    }
}
