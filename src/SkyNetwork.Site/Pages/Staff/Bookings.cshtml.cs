using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class BookingsModel(CurrentUser me, ContentService content) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Bookings;

    public IReadOnlyList<Booking> List { get; private set; } = [];

    public void OnGet() => List = content.Bookings();

    public IActionResult OnPost(long id)
    {
        content.DeleteBooking(id, staffActor: Me.Cid);
        return Redirect("/staff/bookings");
    }
}
