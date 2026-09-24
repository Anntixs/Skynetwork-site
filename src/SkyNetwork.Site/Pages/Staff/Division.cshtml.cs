using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class DivisionModel(CurrentUser me, DivisionService divisions) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ManageDivisions;

    public Division Division { get; private set; } = new();
    /// <summary>A key just issued: shown once, never stored in plain text.</summary>
    public string? NewKey { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    private bool Load(long id)
    {
        if (divisions.Find(id) is not { } d) return false;
        Division = d;
        return true;
    }

    public IActionResult OnGet(long id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPost(long id, string? name, string? region, string? website, string? description, long? director, string? active)
    {
        Error = divisions.Update(Me.Cid, id, name ?? "", region ?? "", website ?? "", description ?? "", director is > 0 ? director : null, active == "true");
        if (Error == null) Message = "Division saved";
        return Load(id) ? Page() : NotFound();
    }

    public IActionResult OnPostKey(long id)
    {
        if (!Load(id)) return NotFound();
        NewKey = divisions.IssueKey(Me.Cid, id);
        Load(id);
        return Page();
    }

    public IActionResult OnPostRevoke(long id)
    {
        divisions.RevokeKey(Me.Cid, id);
        Message = "The API key is revoked";
        return Load(id) ? Page() : NotFound();
    }
}
