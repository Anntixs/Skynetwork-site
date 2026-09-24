using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Account;

public sealed class IndexModel(CurrentUser me, SessionService sessions, FlightPlanService plans) : PageModel
{
    public bool Welcome { get; private set; }
    public MemberHours Hours { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<NetworkSession> Sessions { get; private set; } = [];
    public FlightPlan? Plan { get; private set; }

    public void OnGet(int? welcome)
    {
        Welcome = welcome == 1;
        Hours = sessions.Hours(me.Cid);
        Sessions = sessions.Recent(me.Cid, 15);
        Plan = plans.Latest(me.Cid);
    }
}
