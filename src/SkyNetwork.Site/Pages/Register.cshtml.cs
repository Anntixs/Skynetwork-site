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
        Country = Country.Trim();
        Error = Validate();
        if (Error != null) return Page();
        long cid = members.Register(Name, Email, Country, Password);
        await HttpContext.SignInMemberAsync(members.Find(cid)!, remember: true);
        return Redirect("/account?welcome=1");
    }

    private string? Validate()
    {
        if (Name.Length < 3 || !Name.Contains(' ')) return "Укажите имя и фамилию";
        if (Name.Any(char.IsControl) || Name.Contains(':')) return "Имя содержит недопустимые символы";
        if (!MailAddress.TryCreate(Email, out _)) return "Проверьте адрес почты";
        if (members.EmailTaken(Email)) return "Эта почта уже зарегистрирована. Забыли CID — напишите в поддержку";
        if (Password.Length < 8) return "Пароль — не короче 8 символов";
        if (Password.Contains(':')) return "Пароль не может содержать двоеточие";
        if (Password != Confirm) return "Пароли не совпадают";
        if (!AcceptRules) return "Нужно согласиться с правилами";
        return null;
    }
}
