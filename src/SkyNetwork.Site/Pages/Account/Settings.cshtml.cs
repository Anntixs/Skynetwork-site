using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Account;

public sealed class SettingsModel(CurrentUser me, MemberService members, ConnectService connect) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Country { get; set; } = "";
    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    public string? Message { get; private set; }
    /// <summary>Sites the member signed in to with SkyNetwork Connect.</summary>
    public IReadOnlyList<ConnectConsent> ConnectedSites { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet()
    {
        ConnectedSites = connect.Consents(me.Cid);
        Email = me.Member!.Email ?? "";
        Country = me.Member.Country;
    }

    public IActionResult OnPostProfile()
    {
        Email = Email.Trim();
        Country = Countries.Normalize(Country) ?? Country.Trim();
        if (!MailAddress.TryCreate(Email, out _)) Error = "Check the email address";
        // A country typed before the list existed can stay as it is.
        else if (Country.Length > 0 && Countries.Normalize(Country) == null && Country != me.Member!.Country) Error = "Choose a country from the list";
        else if (!Email.Equals(me.Member!.Email, StringComparison.OrdinalIgnoreCase) && members.EmailTaken(Email)) Error = "This email is already in use";
        else
        {
            members.UpdateProfile(me.Cid, Email, Country);
            Message = "Profile saved";
        }
        return Page();
    }

    public IActionResult OnPostPassword()
    {
        OnGet();
        if (members.Authenticate(me.Cid, Current) == null) Error = "The current password is wrong";
        else if (NewPassword.Length < 8) Error = "The new password must be at least 8 characters";
        else if (NewPassword.Contains(':')) Error = "The password cannot contain a colon";
        else if (NewPassword != Confirm) Error = "The passwords do not match";
        else
        {
            members.ChangePassword(me.Cid, NewPassword);
            Message = "Password changed. Use the new password to connect to the network too";
        }
        return Page();
    }

    public void OnPostRevoke(string clientId)
    {
        connect.Revoke(me.Cid, clientId);
        Message = "Access withdrawn";
        OnGet();
    }
}
