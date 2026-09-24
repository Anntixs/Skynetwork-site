using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class OnlineModel(CurrentUser me, NetworkFeed feed, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Online;

    public OnlineSnapshot Online { get; private set; } = OnlineSnapshot.Empty;
    /// <summary>Member ratings of the controllers online, to spot someone working above their rating.</summary>
    public Dictionary<long, int> Ratings { get; private set; } = [];

    public void OnGet()
    {
        Online = feed.Current;
        foreach (var c in Online.Controllers)
            if (members.Find(c.Cid) is { } m) Ratings[c.Cid] = m.NetworkRating;
    }
}
