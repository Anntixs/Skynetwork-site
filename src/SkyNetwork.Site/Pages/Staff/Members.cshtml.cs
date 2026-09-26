using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Localization;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class MembersModel(CurrentUser me, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ViewMembers;

    /// <summary>At most this many members are listed, the newest first; a search finds the others.</summary>
    public const int Limit = 100;

    public string Query { get; private set; } = "";
    /// <summary>"" for everyone, "suspicious" (name or email would not pass registration today) or "suspended".</summary>
    public string Filter { get; private set; } = "";
    public IReadOnlyList<Member> Results { get; private set; } = [];
    /// <summary>More members match than are listed.</summary>
    public bool Truncated { get; private set; }
    public long Total { get; private set; }
    public bool CanSuspend => Me.Has(Perm.Suspend);
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    // Older links (?suspended=1, ?suspicious=1) still work.
    public void OnGet(string? q, string? show, int? suspended, int? suspicious) => Find(q, Show(show, suspended, suspicious));

    private static string Show(string? show, int? suspended, int? suspicious) =>
        show is "suspicious" or "suspended" ? show : suspicious == 1 ? "suspicious" : suspended == 1 ? "suspended" : "";

    private void Find(string? q, string filter)
    {
        Query = (q ?? "").Trim();
        Filter = filter;
        Total = members.Count();
        var found = members.Search(Query, filter == "suspended", filter == "suspicious" ? 5000 : Limit + 1);
        if (filter == "suspicious") found = found.Where(m => SignupGuard.Suspicion(m) != null).ToList();
        Truncated = found.Count > Limit;
        Results = found.Take(Limit).ToList();
    }

    /// <summary>Suspends the ticked members for good, with the checks of a single suspension (junk registrations).</summary>
    public IActionResult OnPostSuspend(long[] cids, string? reason, string? q, string? show, int? suspended, int? suspicious)
    {
        if (!CanSuspend) return NotFound();
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) Error = "Enter a reason";
        else
        {
            int done = 0, skipped = 0;
            foreach (long cid in cids.Distinct())
            {
                if (members.Find(cid) is not { } m || m.Suspended || !Permissions.CanSuspend(Me.Member!, Me.Permissions, m))
                {
                    skipped++;
                    continue;
                }
                members.SetSuspended(Me.Cid, cid, true, reason);
                done++;
            }
            Message = this.T("Suspended: {0}. Skipped: {1}", done, skipped);
        }
        Find(q, Show(show, suspended, suspicious));
        return Page();
    }
}
