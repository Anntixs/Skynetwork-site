using Microsoft.Extensions.Options;

namespace SkyNetwork.Site.Services;

/// <summary>
/// Map tiles served by the site itself: fetched from the first tile source that answers and kept in
/// a disk cache. Visitors' browsers never contact the tile providers, so the map works where they
/// are blocked or slow, and every tile is downloaded once per week at most.
/// </summary>
public sealed class TileProxy(IHttpClientFactory http, IOptions<SiteOptions> options, IWebHostEnvironment env, ILogger<TileProxy> log)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <param name="labels">The label layer instead of the base map.</param>
    public async Task<IResult> GetAsync(bool labels, int z, int x, int y, CancellationToken ct)
    {
        if (z < 0 || z > 18 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
        string dir = labels ? Path.Combine(CacheDir, "labels") : CacheDir;
        string file = Path.Combine(dir, z.ToString(), x.ToString(), y + ".png");
        var info = new FileInfo(file);
        if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge) return Tile(file);

        var client = http.CreateClient("tiles");
        var sources = labels
            ? options.Value.TileLabelSources is { Length: > 0 } l ? l : SiteOptions.DefaultTileLabelSources
            : options.Value.TileSources is { Length: > 0 } b ? b : SiteOptions.DefaultTileSources;
        foreach (var source in sources)
        {
            string url = source.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            try
            {
                using var r = await client.GetAsync(url, ct);
                if (!r.IsSuccessStatusCode) continue;
                var bytes = await r.Content.ReadAsByteArrayAsync(ct);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, file, overwrite: true);
                return Tile(file);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
            {
                log.LogWarning("Tile {Url}: {Error}", url, ex.Message);
            }
        }
        // All sources failed: an old copy is better than a hole in the map.
        return info.Exists ? Tile(file) : Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private string CacheDir
    {
        get
        {
            string dir = options.Value.TileCache;
            if (dir.Length == 0)
            {
                // Next to the database by default (a directory the site can write to).
                string db = Path.GetFullPath(options.Value.Database, env.ContentRootPath);
                dir = Path.Combine(Path.GetDirectoryName(db)!, "tiles");
            }
            return Path.GetFullPath(dir, env.ContentRootPath);
        }
    }

    // Sources answer with PNG or JPEG; the cached file keeps the bytes as they came.
    private static IResult Tile(string file)
    {
        var head = new byte[2];
        using (var f = File.OpenRead(file)) f.ReadAtLeast(head, 2, throwOnEndOfStream: false);
        return Results.File(file, head is [0xFF, 0xD8] ? "image/jpeg" : "image/png");
    }
}
