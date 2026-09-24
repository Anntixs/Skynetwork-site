using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class DivisionsModel(CurrentUser me, DivisionService divisions) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ManageDivisions;

    public IReadOnlyList<Division> List { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet() => List = divisions.All();

    public IActionResult OnPost(string? code, string? name, string? region, string? website, string? description)
    {
        var (id, error) = divisions.Create(Me.Cid, code ?? "", name ?? "", region ?? "", website ?? "", description ?? "");
        if (error == null) return Redirect($"/staff/divisions/{id}");
        Error = error;
        OnGet();
        return Page();
    }
}
