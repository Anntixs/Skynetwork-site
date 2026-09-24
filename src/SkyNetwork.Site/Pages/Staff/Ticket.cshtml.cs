using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class TicketModel(CurrentUser me, SupportService support) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Tickets;

    public Ticket Ticket { get; private set; } = new();
    public IReadOnlyList<TicketMessage> Messages { get; private set; } = [];

    private bool Load(long id)
    {
        if (support.Ticket(id) is not { } t) return false;
        Ticket = t;
        Messages = support.Messages(id);
        return true;
    }

    public IActionResult OnGet(long id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPostReply(long id, string body)
    {
        if (!Load(id)) return NotFound();
        if (!string.IsNullOrWhiteSpace(body)) support.Reply(id, Me.Cid, staff: true, body.Trim());
        return Redirect($"/staff/tickets/{id}");
    }

    public IActionResult OnPostStatus(long id, string status)
    {
        if (!Load(id)) return NotFound();
        support.SetTicketStatus(Me.Cid, id, status);
        return Redirect($"/staff/tickets/{id}");
    }
}
