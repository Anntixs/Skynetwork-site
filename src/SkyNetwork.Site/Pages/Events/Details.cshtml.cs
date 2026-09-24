using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Events;

public sealed class DetailsModel(ContentService content, CurrentUser me) : PageModel
{
    public NetworkEvent Event { get; private set; } = new();

    public IActionResult OnGet(long id)
    {
        // Drafts are visible to event staff only.
        if (content.Event(id) is not { } e || (!e.Published && !me.Has(Perm.Events))) return NotFound();
        Event = e;
        return Page();
    }
}
