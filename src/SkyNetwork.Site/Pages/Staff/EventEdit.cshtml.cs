using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class EventEditModel(CurrentUser me, ContentService content, UploadStore uploads) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Events;

    public NetworkEvent Event { get; private set; } = new();
    public string? Error { get; private set; }

    /// <summary>The language tab that is open: "ru" or "en".</summary>
    public string EditLang { get; private set; } = "ru";

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

    public async Task<IActionResult> OnPostAsync(string id, string? title, string? summary, string? body, string? titleEn, string? summaryEn,
        string? bodyEn, string? airports, string startDate, string startTime, string endDate, string endTime, bool published,
        IFormFile? banner, bool removeBanner, IFormFile? bannerEn, bool removeBannerEn, string? bannerSize, string? bannerFocus, string? editLang)
    {
        if (!Load(id)) return NotFound();
        EditLang = editLang == "en" ? "en" : "ru";
        long? start = Format.ParseUtc(startDate, startTime), end = Format.ParseUtc(endDate, endTime);
        Event.Title = (title ?? "").Trim();
        Event.Summary = (summary ?? "").Trim();
        Event.Body = (body ?? "").Trim();
        Event.TitleEn = (titleEn ?? "").Trim();
        Event.SummaryEn = (summaryEn ?? "").Trim();
        Event.BodyEn = (bodyEn ?? "").Trim();
        Event.Airports = string.Join(' ', (airports ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Event.Published = published;
        Event.BannerSize = BannerLayout.Size(bannerSize);
        Event.BannerFocus = BannerLayout.Focus(bannerFocus);
        // Russian, English or both: a version that is begun needs its title, and the tab that lacks it opens.
        bool ru = Event.Title.Length + Event.Summary.Length + Event.Body.Length > 0;
        bool en = Event.TitleEn.Length + Event.SummaryEn.Length + Event.BodyEn.Length > 0;
        string? untitled = ru && Event.Title.Length < 3 ? "ru" : en && Event.TitleEn.Length < 3 ? "en" : null;
        if (untitled != null || !ru && !en)
        {
            Error = "Enter a title";
            EditLang = untitled ?? EditLang;
        }
        else if (start == null || end == null || end <= start) Error = "Check the start and end times";
        if (Error != null) return Page();
        Event.StartsAt = start!.Value;
        Event.EndsAt = end!.Value;
        // Banners: a new upload replaces one, its checkbox removes it; the replaced files go once the event is saved.
        string oldRu = Event.Banner, oldEn = Event.BannerEn;
        var (ruBanner, error) = await uploads.ReplaceAsync(banner, removeBanner, oldRu, HttpContext.RequestAborted);
        string enBanner = oldEn;
        if (error != null) EditLang = "ru";
        else
        {
            (enBanner, error) = await uploads.ReplaceAsync(bannerEn, removeBannerEn, oldEn, HttpContext.RequestAborted);
            if (error != null) EditLang = "en";
        }
        if (error != null)
        {
            uploads.Delete(ruBanner == oldRu ? null : ruBanner);
            Error = error;
            return Page();
        }
        (Event.Banner, Event.BannerEn) = (ruBanner, enBanner);
        if (ruBanner.Length == 0 && enBanner.Length == 0) Event.BannerSize = Event.BannerFocus = "";
        long saved = content.SaveEvent(Me.Cid, Event);
        foreach (string old in new[] { oldRu, oldEn })
            if (old != ruBanner && old != enBanner) uploads.Delete(old);
        return Redirect($"/staff/events/{saved}");
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!Load(id) || Event.Id == 0) return NotFound();
        content.DeleteEvent(Me.Cid, Event.Id);
        uploads.Delete(Event.Banner);
        uploads.Delete(Event.BannerEn);
        return Redirect("/staff/events");
    }
}
