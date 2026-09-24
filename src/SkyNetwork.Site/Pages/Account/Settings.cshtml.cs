using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Account;

public sealed class SettingsModel(CurrentUser me, MemberService members) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Country { get; set; } = "";
    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        Email = me.Member!.Email ?? "";
        Country = me.Member.Country;
    }

    public IActionResult OnPostProfile()
    {
        Email = Email.Trim();
        Country = Country.Trim();
        if (!MailAddress.TryCreate(Email, out _)) Error = "Проверьте адрес почты";
        else if (!Email.Equals(me.Member!.Email, StringComparison.OrdinalIgnoreCase) && members.EmailTaken(Email)) Error = "Эта почта уже используется";
        else
        {
            members.UpdateProfile(me.Cid, Email, Country);
            Message = "Профиль сохранён";
        }
        return Page();
    }

    public IActionResult OnPostPassword()
    {
        OnGet();
        if (members.Authenticate(me.Cid, Current) == null) Error = "Текущий пароль неверен";
        else if (NewPassword.Length < 8) Error = "Новый пароль — не короче 8 символов";
        else if (NewPassword.Contains(':')) Error = "Пароль не может содержать двоеточие";
        else if (NewPassword != Confirm) Error = "Пароли не совпадают";
        else
        {
            members.ChangePassword(me.Cid, NewPassword);
            Message = "Пароль изменён. Используйте новый пароль и для подключения к сети";
        }
        return Page();
    }
}
