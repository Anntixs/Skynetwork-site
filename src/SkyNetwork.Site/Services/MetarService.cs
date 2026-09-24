using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SkyNetwork.Site.Services;

/// <summary>Current METAR of an airport from the Aviation Weather Center (NOAA), cached for a few minutes.</summary>
public sealed partial class MetarService(IHttpClientFactory http, ILogger<MetarService> log)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (DateTime At, string Text)> _cache = new();

    [GeneratedRegex("^[A-Z0-9]{4}$")] private static partial Regex Icao();

    /// <returns>The METAR text, "" when the airport has none, null for a malformed code.</returns>
    public async Task<string?> GetAsync(string icao, CancellationToken ct)
    {
        icao = icao.ToUpperInvariant();
        if (!Icao().IsMatch(icao)) return null;
        if (_cache.TryGetValue(icao, out var hit) && DateTime.UtcNow - hit.At < MaxAge) return hit.Text;
        try
        {
            string text = (await http.CreateClient("metar")
                .GetStringAsync($"https://aviationweather.gov/api/data/metar?ids={icao}&format=raw", ct)).Trim();
            // One report per line; the first is the latest.
            text = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            _cache[icao] = (DateTime.UtcNow, text);
            if (_cache.Count > 5000) _cache.Clear();
            return text;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("METAR {Icao}: {Error}", icao, e.Message);
            return hit.Text ?? "";
        }
    }
}
