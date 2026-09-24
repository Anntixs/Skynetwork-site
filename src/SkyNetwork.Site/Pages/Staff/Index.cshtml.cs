using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class IndexModel(CurrentUser me, MemberService members, SupportService support, SessionService sessions,
    AuditService audit, NetworkFeed feed, DivisionService divisions) : StaffPageModel(me)
{
    protected override Perm Required => Perm.StaffArea;

    public long Members { get; private set; }
    public OnlineSnapshot Online { get; private set; } = OnlineSnapshot.Empty;
    public int OpenTickets { get; private set; }
    public int OpenTraining { get; private set; }
    public int PendingRatings { get; private set; }
    public IReadOnlyList<TopEntry> TopPilots { get; private set; } = [];
    public IReadOnlyList<TopEntry> TopAtc { get; private set; } = [];
    public IReadOnlyList<AuditEntry> Audit { get; private set; } = [];

    public void OnGet()
    {
        Members = members.Count();
        Online = feed.Current;
        (OpenTickets, OpenTraining) = support.Counts();
        PendingRatings = divisions.PendingCount();
        TopPilots = sessions.Top("pilot");
        TopAtc = sessions.Top("atc");
        if (Me.Has(Perm.Audit)) Audit = audit.Recent(15);
    }
}
