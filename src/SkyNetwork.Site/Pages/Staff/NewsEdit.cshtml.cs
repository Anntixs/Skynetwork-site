using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class NewsEditModel(CurrentUser me, ContentService content, UploadStore uploads) : StaffPageModel(me)
{
    protected override Perm Required => Perm.News;

    public NewsPost Post { get; private set; } = new();
    public string? Error { get; private set; }

    /// <summary>The language tab that is open: "ru" or "en".</summary>
    public string EditLang { get; private set; } = "ru";

    private bool Load(string id)
    {
        if (id == "new")
        {
            Post = new NewsPost { Published = true };
            return true;
        }
        if (!long.TryParse(id, out var n) || content.Post(n) is not { } p) return false;
        Post = p;
        return true;
    }

    public IActionResult OnGet(string id) => Load(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string id, string? title, string? body, string? titleEn, string? bodyEn, bool published,
        IFormFile? banner, bool removeBanner, IFormFile? bannerEn, bool removeBannerEn, string? bannerSize, string? bannerFocus, string? editLang)
    {
        if (!Load(id)) return NotFound();
        EditLang = editLang == "en" ? "en" : "ru";
        Post.Title = (title ?? "").Trim();
        Post.Body = (body ?? "").Trim();
        Post.TitleEn = (titleEn ?? "").Trim();
        Post.BodyEn = (bodyEn ?? "").Trim();
        Post.Published = published;
        Post.BannerSize = BannerLayout.Size(bannerSize);
        Post.BannerFocus = BannerLayout.Focus(bannerFocus);
        // Russian, English or both: a version that is begun needs its headline and text, and the tab with the gap opens.
        bool ru = Post.Title.Length + Post.Body.Length > 0, en = Post.TitleEn.Length + Post.BodyEn.Length > 0;
        string? unfinished = ru && (Post.Title.Length < 3 || Post.Body.Length < 3) ? "ru"
            : en && (Post.TitleEn.Length < 3 || Post.BodyEn.Length < 3) ? "en" : null;
        if (unfinished != null || !ru && !en)
        {
            Error = "Fill in the headline and text";
            EditLang = unfinished ?? EditLang;
            return Page();
        }
        // Banners: a new upload replaces one, its checkbox removes it; the replaced files go once the post is saved.
        string oldRu = Post.Banner, oldEn = Post.BannerEn;
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
        (Post.Banner, Post.BannerEn) = (ruBanner, enBanner);
        if (ruBanner.Length == 0 && enBanner.Length == 0) Post.BannerSize = Post.BannerFocus = "";
        long saved = content.SavePost(Me.Cid, Post);
        foreach (string old in new[] { oldRu, oldEn })
            if (old != ruBanner && old != enBanner) uploads.Delete(old);
        return Redirect($"/staff/news/{saved}");
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!Load(id) || Post.Id == 0) return NotFound();
        content.DeletePost(Me.Cid, Post.Id);
        uploads.Delete(Post.Banner);
        uploads.Delete(Post.BannerEn);
        return Redirect("/staff/news");
    }
}
