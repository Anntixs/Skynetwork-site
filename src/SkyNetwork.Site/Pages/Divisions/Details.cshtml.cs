using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Divisions;

public sealed class DetailsModel(DivisionService divisions, CurrentUser me) : PageModel
{
    public Division Division { get; private set; } = new();
    public bool Mine { get; private set; }

    public IActionResult OnGet(string code)
    {
        if (divisions.FindByCode(code) is not { Active: true } d) return NotFound();
        Division = d;
        Mine = me.Member != null && divisions.Of(me.Cid)?.Id == d.Id;
        return Page();
    }
}
