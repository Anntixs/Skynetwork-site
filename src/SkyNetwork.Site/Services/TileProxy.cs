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

    /// <param name="style">"light" or "dark" (the site theme).</param>
    public async Task<IResult> GetAsync(string style, int z, int x, int y, CancellationToken ct)
    {
        if (style is not ("light" or "dark") || z < 0 || z > 18 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
        string file = Path.Combine(CacheDir, style, z.ToString(), x.ToString(), y + ".png");
        var info = new FileInfo(file);
        if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge) return Tile(file);

        var client = http.CreateClient("tiles");
        var configured = style == "dark" ? options.Value.DarkTileSources : options.Value.TileSources;
        var sources = configured is { Length: > 0 } ? configured : style == "dark" ? SiteOptions.DefaultDarkTileSources : SiteOptions.DefaultTileSources;
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

    private static IResult Tile(string file) => Results.File(file, "image/png");
}
