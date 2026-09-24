using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Account;

public sealed class SupportModel(CurrentUser me, SupportService support) : PageModel
{
    [BindProperty] public string Subject { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    public string? Error { get; private set; }
    public IReadOnlyList<Ticket> Tickets { get; private set; } = [];

    public void OnGet() => Tickets = support.Tickets(me.Cid);

    public IActionResult OnPost()
    {
        if (Subject.Trim().Length < 3 || Body.Trim().Length < 10)
        {
            Error = "Опишите вопрос подробнее";
            OnGet();
            return Page();
        }
        long id = support.OpenTicket(me.Cid, me.Member!.Email ?? "", Subject.Trim(), Body.Trim());
        return Redirect($"/account/support/{id}");
    }
}
