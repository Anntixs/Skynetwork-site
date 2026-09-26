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

    public async Task<IActionResult> OnPostAsync(string id, string title, string body, bool published, IFormFile? banner, bool removeBanner,
        string? bannerSize, string? bannerFocus)
    {
        if (!Load(id)) return NotFound();
        Post.Title = (title ?? "").Trim();
        Post.Body = (body ?? "").Trim();
        Post.Published = published;
        Post.BannerSize = BannerLayout.Size(bannerSize);
        Post.BannerFocus = BannerLayout.Focus(bannerFocus);
        if (Post.Title.Length < 3 || Post.Body.Length < 3)
        {
            Error = "Fill in the headline and text";
            return Page();
        }
        string old = Post.Banner;
        if (banner is { Length: > 0 })
        {
            var (name, error) = await uploads.SaveImageAsync(banner, HttpContext.RequestAborted);
            if (error != null)
            {
                Error = error;
                return Page();
            }
            Post.Banner = name!;
        }
        else if (removeBanner)
        {
            Post.Banner = "";
            Post.BannerSize = Post.BannerFocus = "";
        }
        long saved = content.SavePost(Me.Cid, Post);
        if (old.Length > 0 && old != Post.Banner) uploads.Delete(old);
        return Redirect($"/staff/news/{saved}");
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!Load(id) || Post.Id == 0) return NotFound();
        content.DeletePost(Me.Cid, Post.Id);
        uploads.Delete(Post.Banner);
        return Redirect("/staff/news");
    }
}
