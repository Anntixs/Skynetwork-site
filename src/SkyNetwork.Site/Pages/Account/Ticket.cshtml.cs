using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Account;

public sealed class TicketModel(CurrentUser me, SupportService support) : PageModel
{
    public Ticket Ticket { get; private set; } = new();
    public IReadOnlyList<TicketMessage> Messages { get; private set; } = [];

    private bool Load(long id)
    {
        // Members only ever see their own tickets.
        if (support.Ticket(id) is not { } t || t.Cid != me.Cid) return false;
        Ticket = t;
        Messages = support.Messages(id);
        return true;
    }

    public IActionResult OnGet(long id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPost(long id, string body)
    {
        if (!Load(id)) return NotFound();
        if (Ticket.Status != "closed" && !string.IsNullOrWhiteSpace(body)) support.Reply(id, me.Cid, staff: false, body.Trim());
        return Redirect($"/account/support/{id}");
    }
}
