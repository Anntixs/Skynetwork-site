using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

[EnableRateLimiting("auth")]
public sealed class LoginModel(MemberService members) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public long Cid { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool Remember { get; set; }
    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var m = members.Authenticate(Cid, Password);
        if (m == null)
        {
            // Only someone who knows the password learns that the account is suspended.
            Error = members.IsSuspended(Cid) && members.PasswordMatches(Cid, Password)
                ? "Учётная запись заблокирована. Подробности — в поддержке"
                : "Неверный CID или пароль";
            return Page();
        }
        await HttpContext.SignInMemberAsync(m, Remember);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/account");
    }
}
