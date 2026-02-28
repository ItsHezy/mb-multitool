using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Geo.Providers;

public interface IGeoProvider
{
    string Name { get; }

    Task<GeoResult?> LookupAsync(string ipOrSelf, CancellationToken cancellationToken = default);
}
