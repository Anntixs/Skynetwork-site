using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

[EnableRateLimiting("auth")]
public sealed class RegisterModel(MemberService members) : PageModel
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Country { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    [BindProperty] public bool AcceptRules { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/account") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        Name = Name.Trim();
        Email = Email.Trim();
        Country = Countries.Normalize(Country) ?? Country.Trim();
        Error = Validate();
        if (Error != null) return Page();
        long cid = members.Register(Name, Email, Country, Password);
        await HttpContext.SignInMemberAsync(members.Find(cid)!, remember: true);
        return Redirect("/account?welcome=1");
    }

    private string? Validate()
    {
        if (MemberService.ValidateName(Name) is { } nameError) return nameError;
        if (!MailAddress.TryCreate(Email, out _)) return "Check the email address";
        if (members.EmailTaken(Email)) return "This email is already registered. Forgot your CID? Write to support";
        if (Countries.Normalize(Country) == null) return "Choose a country from the list";
        if (Password.Length < 8) return "The password must be at least 8 characters";
        if (Password.Contains(':')) return "The password cannot contain a colon";
        if (Password != Confirm) return "The passwords do not match";
        if (!AcceptRules) return "You need to accept the rules";
        return null;
    }
}
