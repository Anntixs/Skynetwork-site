using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Localization;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

/// <summary>Rating requests divisions sent after exams: supervisors approve or decline them.</summary>
public sealed class RatingsModel(CurrentUser me, DivisionService divisions) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ApproveRatings;

    public bool All { get; private set; }
    public IReadOnlyList<RatingRequest> List { get; private set; } = [];
    /// <summary>Divisions and their API keys (administrators only).</summary>
    public IReadOnlyList<Division> Keys { get; private set; } = [];
    /// <summary>A key just issued: shown once, never stored in plain text.</summary>
    public string? NewKey { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public bool CanApprove(RatingRequest r) =>
        r.Cid != Me.Cid && Permissions.CanApproveRating(Me.Member!.StaffRank, Me.Permissions, r.Track, r.TargetRating);

    public void OnGet(int? all)
    {
        All = all == 1;
        List = divisions.Requests(All ? null : "pending");
        if (Me.Has(Perm.ManageDivisions)) Keys = divisions.All();
    }

    public void OnPostApprove(long id, string? comment)
    {
        var r = divisions.Request(id);
        Error = divisions.Approve(Me.Member!, Me.Permissions, id, comment ?? "");
        if (Error == null && r != null) Message = this.T("{0}: {1} granted", r.Name, r.TargetShort);
        OnGet(null);
    }

    public void OnPostDecline(long id, string? comment)
    {
        Error = divisions.Decline(Me.Member!, Me.Permissions, id, comment ?? "");
        if (Error == null) Message = "Request declined";
        OnGet(null);
    }

    public IActionResult OnPostKey(string? divisionCode, string? name)
    {
        if (!Me.Has(Perm.ManageDivisions)) return NotFound();
        (NewKey, Error) = divisions.IssueKey(Me.Cid, divisionCode ?? "", name ?? "");
        OnGet(null);
        return Page();
    }

    public IActionResult OnPostRevoke(long id)
    {
        if (!Me.Has(Perm.ManageDivisions)) return NotFound();
        divisions.RevokeKey(Me.Cid, id);
        Message = "The API key is revoked";
        OnGet(null);
        return Page();
    }
}
