using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class EventsModel(CurrentUser me, ContentService content) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Events;

    public IReadOnlyList<NetworkEvent> List { get; private set; } = [];

    public void OnGet() => List = content.AllEvents();
}
