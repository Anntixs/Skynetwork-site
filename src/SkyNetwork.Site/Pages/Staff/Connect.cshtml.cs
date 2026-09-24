using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

/// <summary>Sites allowed to sign members in with SkyNetwork Connect (administrators).</summary>
public sealed class ConnectModel(CurrentUser me, ConnectService connect) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ManageDivisions;

    public IReadOnlyList<ConnectClient> List { get; private set; } = [];
    /// <summary>Client id and secret just issued: the secret is shown once.</summary>
    public (string ClientId, string Secret)? Issued { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet() => List = connect.Clients();

    public void OnPost(string? name, string? redirects)
    {
        var (id, secret, error) = connect.CreateClient(Me.Cid, name ?? "", redirects ?? "");
        if (error != null) Error = error;
        else Issued = (id!, secret!);
        OnGet();
    }

    public void OnPostUpdate(long id, string? name, string? redirects, string? active)
    {
        Error = connect.UpdateClient(Me.Cid, id, name ?? "", redirects ?? "", active == "true");
        if (Error == null) Message = "Site saved";
        OnGet();
    }

    public IActionResult OnPostSecret(long id)
    {
        if (connect.Client(id) is not { } c) return NotFound();
        Issued = (c.ClientId, connect.NewClientSecret(Me.Cid, id)!);
        OnGet();
        return Page();
    }
}
