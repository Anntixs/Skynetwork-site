namespace SkyNetwork.Site.Data;

/// <summary>
/// How the banner of an event or a news post is shown, chosen in the editor: its height on its own page and the part of
/// the picture kept in view when it is cropped. Stored as short words; "" is the look banners had before there was a
/// choice (16:6 on the page, cropped around the centre).
/// </summary>
public static class BannerLayout
{
    /// <summary>A low strip, the standard height, tall (16:9), or the whole picture uncropped (on cards too).</summary>
    public static readonly string[] Sizes = ["strip", "", "tall", "full"];

    /// <summary>The part of the picture kept in view when it is cropped, on its page and on cards.</summary>
    public static readonly string[] Focuses = ["top", "", "bottom"];

    public static string Size(string? value) => Pick(Sizes, value);

    public static string Focus(string? value) => Pick(Focuses, value);

    /// <summary>CSS classes of the banner and its cards, e.g. "banner-full focus-top"; null for the standard look.</summary>
    public static string? Classes(string? size, string? focus)
    {
        string s = Size(size), f = Focus(focus);
        string classes = (s.Length > 0 ? "banner-" + s : "") + (s.Length > 0 && f.Length > 0 ? " " : "") + (f.Length > 0 ? "focus-" + f : "");
        return classes.Length > 0 ? classes : null;
    }

    private static string Pick(string[] allowed, string? value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        return allowed.Contains(value) ? value : "";
    }
}

/// <summary>
/// The banner part of the staff editors of events and news (<c>Pages/Staff/_BannerFields.cshtml</c>): the Russian and the
/// English picture, the layout they share, and the language tab that is open ("ru" or "en").
/// </summary>
public sealed record BannerFields(string? Url, string? UrlEn, string Size, string Focus, string Lang, bool ForEvent)
{
    public string? Classes => BannerLayout.Classes(Size, Focus);

    /// <summary>The picture of the open tab, or the other language's one (as each site shows it).</summary>
    public string? Shown => Lang == "en" ? UrlEn ?? Url : Url ?? UrlEn;
}
