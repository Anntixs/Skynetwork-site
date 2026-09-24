using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class EventEditModel(CurrentUser me, ContentService content) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Events;

    public NetworkEvent Event { get; private set; } = new();
    public string? Error { get; private set; }

    private bool Load(string id)
    {
        if (id == "new")
        {
            var start = DateTime.UtcNow.Date.AddDays(7).AddHours(16);
            Event = new NetworkEvent
            {
                Published = true,
                StartsAt = new DateTimeOffset(start).ToUnixTimeSeconds(),
                EndsAt = new DateTimeOffset(start.AddHours(4)).ToUnixTimeSeconds(),
            };
            return true;
        }
        if (!long.TryParse(id, out var n) || content.Event(n) is not { } e) return false;
        Event = e;
        return true;
    }

    public IActionResult OnGet(string id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPost(string id, string title, string? summary, string? airports, string startDate, string startTime,
        string endDate, string endTime, string? body, bool published)
    {
        if (!Load(id)) return NotFound();
        long? start = Format.ParseUtc(startDate, startTime), end = Format.ParseUtc(endDate, endTime);
        Event.Title = (title ?? "").Trim();
        Event.Summary = (summary ?? "").Trim();
        Event.Airports = string.Join(' ', (airports ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Event.Body = (body ?? "").Trim();
        Event.Published = published;
        if (Event.Title.Length < 3) Error = "Укажите название";
        else if (start == null || end == null || end <= start) Error = "Проверьте время начала и окончания";
        if (Error != null) return Page();
        Event.StartsAt = start!.Value;
        Event.EndsAt = end!.Value;
        long saved = content.SaveEvent(Me.Cid, Event);
        return Redirect($"/staff/events/{saved}");
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!Load(id) || Event.Id == 0) return NotFound();
        content.DeleteEvent(Me.Cid, Event.Id);
        return Redirect("/staff/events");
    }
}
