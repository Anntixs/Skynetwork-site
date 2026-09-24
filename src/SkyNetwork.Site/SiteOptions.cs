namespace SkyNetwork.Site;

/// <summary>Settings from the "Site" section of appsettings.json.</summary>
public sealed class SiteOptions
{
    public string Name { get; set; } = "SkyNetwork";
    /// <summary>SQLite file shared with the FSD server (its --db): members registered here can log in to the network.</summary>
    public string Database { get; set; } = "skynetwork.db";
    /// <summary>FSD server data feed; empty disables the live data.</summary>
    public string DataFeedUrl { get; set; } = "http://127.0.0.1:8080/data.json";
    public int FeedPollSeconds { get; set; } = 15;
    /// <summary>Shown on the "how to connect" pages.</summary>
    public string FsdHost { get; set; } = "127.0.0.1";
    public int FsdPort { get; set; } = 6809;
    /// <summary>CID given to the first registered member.</summary>
    public int FirstCid { get; set; } = 1;
    /// <summary>
    /// Map tile sources ({z}/{x}/{y}), tried in order (empty: <see cref="DefaultTileSources"/>). The site downloads and caches the tiles itself,
    /// so visitors' browsers only talk to this site.
    /// </summary>
    public string[] TileSources { get; set; } = [];
    public static readonly string[] DefaultTileSources =
    [
        "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
        "https://b.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    ];
    /// <summary>Tile sources for the dark theme (empty: <see cref="DefaultDarkTileSources"/>).</summary>
    public string[] DarkTileSources { get; set; } = [];
    public static readonly string[] DefaultDarkTileSources =
    [
        "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
        "https://b.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    ];
    /// <summary>Tile cache directory; empty means "tiles" next to the database.</summary>
    public string TileCache { get; set; } = "";
    /// <summary>Directory for uploaded banners; empty means "uploads" next to the database.</summary>
    public string Uploads { get; set; } = "";
    /// <summary>Behind nginx or another reverse proxy: trust its X-Forwarded-For / X-Forwarded-Proto headers.</summary>
    public bool BehindProxy { get; set; }
    /// <summary>Login/registration attempts per minute from one address.</summary>
    public int AuthAttemptsPerMinute { get; set; } = 10;
}
