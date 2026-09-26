using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages;

[EnableRateLimiting("auth")]
public sealed class ForgotPasswordModel(MemberService members, AccountMail mail) : PageModel
{
    [BindProperty] public string Login { get; set; } = "";
    public bool Sent { get; private set; }
    public bool Enabled => mail.Enabled;

    public void OnGet() { }

    public IActionResult OnPost()
    {
        if (!Enabled) return Page();
        // The same answer whether the account exists or not: nobody learns whose address is registered.
        if (members.FindByCidOrEmail(Login ?? "") is { Suspended: false } m) mail.SendReset(Request, m);
        Sent = true;
        return Page();
    }
}
