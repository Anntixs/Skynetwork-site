using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class NewsEditModel(CurrentUser me, ContentService content) : StaffPageModel(me)
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

    public IActionResult OnPost(string id, string title, string body, bool published)
    {
        if (!Load(id)) return NotFound();
        Post.Title = (title ?? "").Trim();
        Post.Body = (body ?? "").Trim();
        Post.Published = published;
        if (Post.Title.Length < 3 || Post.Body.Length < 3)
        {
            Error = "Fill in the headline and text";
            return Page();
        }
        long saved = content.SavePost(Me.Cid, Post);
        return Redirect($"/staff/news/{saved}");
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!Load(id) || Post.Id == 0) return NotFound();
        content.DeletePost(Me.Cid, Post.Id);
        return Redirect("/staff/news");
    }
}
