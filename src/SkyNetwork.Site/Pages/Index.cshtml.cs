using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages;

public sealed class IndexModel(NetworkFeed feed, ContentService content, MemberService members, SessionService sessions) : PageModel
{
    public OnlineSnapshot Online { get; private set; } = OnlineSnapshot.Empty;
    public IReadOnlyList<NetworkEvent> Events { get; private set; } = [];
    public IReadOnlyList<NewsPost> News { get; private set; } = [];
    public long MemberCount { get; private set; }
    public int SessionsToday { get; private set; }
    public int SessionsMonth { get; private set; }

    public void OnGet()
    {
        Online = feed.Current;
        Events = content.UpcomingEvents(3);
        News = content.News(3);
        MemberCount = members.Count();
        (SessionsToday, SessionsMonth) = sessions.SessionCounts();
    }
}
