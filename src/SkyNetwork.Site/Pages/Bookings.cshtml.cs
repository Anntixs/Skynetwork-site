using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

public sealed partial class BookingsModel(ContentService content, CurrentUser me) : PageModel
{
    public IReadOnlyList<Booking> List { get; private set; } = [];
    public string? Error { get; private set; }
    public bool Saved { get; private set; }

    public void OnGet(int? saved)
    {
        Saved = saved == 1;
        List = content.Bookings();
    }

    [GeneratedRegex("^[A-Z0-9]{2,4}(_[A-Z0-9]{1,3})?_(DEL|GND|TWR|APP|DEP|CTR|FSS)$")]
    private static partial Regex Position();

    public IActionResult OnPost(string callsign, string date, string from, string to)
    {
        if (me.Member is not { Rating: >= Ratings.S1 }) return Forbid();
        callsign = (callsign ?? "").Trim().ToUpperInvariant();
        long? start = Format.ParseUtc(date, from), end = Format.ParseUtc(date, to);
        if (start is { } s && end is { } e && e <= s) end = e + 86400; // over midnight
        if (!Position().IsMatch(callsign)) Error = "Позиция — например UUEE_TWR или UUWV_CTR";
        else if (start == null || end == null) Error = "Укажите дату и время";
        else Error = content.Book(me.Cid, callsign, start.Value, end.Value);
        if (Error != null)
        {
            OnGet(null);
            return Page();
        }
        return Redirect("/bookings?saved=1");
    }

    public IActionResult OnPostDelete(long id)
    {
        if (content.BookingById(id) is { } b && b.Cid == me.Cid) content.DeleteBooking(id);
        return Redirect("/bookings");
    }
}
