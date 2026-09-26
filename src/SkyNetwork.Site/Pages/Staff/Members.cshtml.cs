using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Localization;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class MembersModel(CurrentUser me, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ViewMembers;

    public string Query { get; private set; } = "";
    public bool SuspendedOnly { get; private set; }
    /// <summary>Only accounts whose name or email would not pass registration today (junk registrations).</summary>
    public bool SuspiciousOnly { get; private set; }
    public IReadOnlyList<Member> Results { get; private set; } = [];
    public bool CanSuspend => Me.Has(Perm.Suspend);
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet(string? q, int? suspended, int? suspicious) => Find(q, suspended == 1, suspicious == 1);

    private void Find(string? q, bool suspendedOnly, bool suspiciousOnly)
    {
        Query = q ?? "";
        SuspendedOnly = suspendedOnly;
        SuspiciousOnly = suspiciousOnly;
        var found = members.Search(q, SuspendedOnly, suspiciousOnly ? 5000 : 100);
        Results = suspiciousOnly ? found.Where(m => SignupGuard.Suspicion(m) != null).ToList() : found;
    }

    /// <summary>Suspends the ticked members for good, with the checks of a single suspension (junk registrations).</summary>
    public IActionResult OnPostSuspend(long[] cids, string? reason, string? q, int? suspended, int? suspicious)
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
        Find(q, suspended == 1, suspicious == 1);
        return Page();
    }
}
