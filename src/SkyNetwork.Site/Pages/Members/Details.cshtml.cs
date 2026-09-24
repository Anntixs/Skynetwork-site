using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Pages.Members;

public sealed class DetailsModel(MemberService members, SessionService sessions, DivisionService divisions) : PageModel
{
    public Member Member { get; private set; } = new();
    public Division? Division { get; private set; }
    public MemberHours Hours { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<NetworkSession> Sessions { get; private set; } = [];

    public IActionResult OnGet(long cid)
    {
        if (members.Find(cid) is not { } m) return NotFound();
        Member = m;
        Division = divisions.Of(cid);
        Hours = sessions.Hours(cid);
        Sessions = sessions.Recent(cid, 20);
        return Page();
    }
}
