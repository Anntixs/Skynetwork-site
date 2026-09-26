using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages;

[EnableRateLimiting("auth")]
public sealed class RegisterModel(MemberService members, SignupGuard guard, AccountMail mail) : PageModel
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Country { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    /// <summary>A field people never see: only bots fill it in.</summary>
    [BindProperty] public string? Website { get; set; }
    /// <summary>When the form was shown (sealed): a form sent within seconds did not come from a person.</summary>
    [BindProperty] public string? Started { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/account");
        Started = guard.Stamp();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // An empty field binds as null.
        Name = SignupGuard.TidyName(Name ?? "");
        Email = (Email ?? "").Trim();
        Country = Countries.Normalize(Country ?? "") ?? (Country ?? "").Trim();
        Password ??= "";
        Confirm ??= "";
        Error = guard.CheckForm(Website, Started) ?? Validate();
        // A few accounts a day from one address: the rest are junk.
        if (Error == null && !guard.TryTakeSlot(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?"))
            Error = "Too many accounts were registered from your address today. Try again tomorrow";
        if (Error != null)
        {
            Started = guard.Stamp();
            return Page();
        }
        // With mail set up the address is confirmed by a link before the member can connect to the network.
        long cid = members.Register(Name, Email, Country, Password, verified: !mail.Enabled);
        var member = members.Find(cid)!;
        mail.SendConfirmation(Request, member, Email);
        await HttpContext.SignInMemberAsync(member, remember: true);
        return Redirect("/account?welcome=1");
    }

    private string? Validate()
    {
        if (SignupGuard.CheckName(Name) is { } nameError) return nameError;
        if (SignupGuard.CheckEmail(Email) is { } emailError) return emailError;
        if (members.EmailTaken(Email)) return "This email is already registered. Forgot your CID? Write to support";
        if (Countries.Normalize(Country) == null) return "Choose a country from the list";
        if (Password.Length < 8) return "The password must be at least 8 characters";
        if (Password.Contains(':')) return "The password cannot contain a colon";
        if (Password != Confirm) return "The passwords do not match";
        return null;
    }
}
