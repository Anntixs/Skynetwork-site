using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.News;

public sealed class DetailsModel(ContentService content, CurrentUser me) : PageModel
{
    public NewsPost Post { get; private set; } = new();

    public IActionResult OnGet(long id)
    {
        if (content.Post(id) is not { } p || (!p.Published && !me.Has(Perm.News))) return NotFound();
        Post = p;
        return Page();
    }
}
