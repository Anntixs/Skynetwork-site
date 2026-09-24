using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages;

public sealed class IndexModel(NetworkFeed feed, ContentService content, MemberService members, SessionService sessions, IWebHostEnvironment env) : PageModel
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public OnlineSnapshot Online { get; private set; } = OnlineSnapshot.Empty;
    public IReadOnlyList<NetworkEvent> Events { get; private set; } = [];
    public IReadOnlyList<NewsPost> News { get; private set; } = [];
    public long MemberCount { get; private set; }
    public int SessionsToday { get; private set; }
    public int SessionsMonth { get; private set; }
    /// <summary>Screenshots in wwwroot/gallery as site URLs, in file-name order; empty when the folder is missing.</summary>
    public IReadOnlyList<string> Gallery { get; private set; } = [];

    public void OnGet()
    {
        Online = feed.Current;
        Events = content.UpcomingEvents(3);
        News = content.News(3);
        MemberCount = members.Count();
        (SessionsToday, SessionsMonth) = sessions.SessionCounts();
        Gallery = GalleryFiles();
    }

    private List<string> GalleryFiles()
    {
        try
        {
            string root = env.WebRootPath;
            if (string.IsNullOrEmpty(root)) return [];
            string dir = Path.Combine(root, "gallery");
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateFiles(dir)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f))
                .Order(StringComparer.Ordinal)
                .Select(n => "/gallery/" + Uri.EscapeDataString(n))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
