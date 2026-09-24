using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class TicketsModel(CurrentUser me, SupportService support) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Tickets;

    public string? Status { get; private set; }
    public IReadOnlyList<Ticket> List { get; private set; } = [];

    public void OnGet(string? status)
    {
        Status = status != null && SupportService.TicketStatuses.ContainsKey(status) ? status : null;
        List = support.Tickets(status: Status);
    }
}
