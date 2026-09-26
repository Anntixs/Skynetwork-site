using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages.Account;

public sealed class IndexModel(CurrentUser me, SessionService sessions, FlightPlanService plans, AccountMail mail) : PageModel
{
    public bool Welcome { get; private set; }
    /// <summary>The confirmation letter was sent again (true) or one went out a moment ago (false).</summary>
    public bool? Resent { get; private set; }
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

    public void OnPostResend()
    {
        OnGet(null);
        var m = me.Member!;
        if (!m.EmailVerified && !string.IsNullOrWhiteSpace(m.Email)) Resent = mail.SendConfirmation(Request, m, m.Email);
    }
}
