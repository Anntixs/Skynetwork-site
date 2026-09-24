using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

/// <summary>
/// Base of every staff page. The middleware already hides /staff from non-staff; each page also
/// needs its own permission and answers 404 (not 403) without it, so nothing reveals what exists.
/// </summary>
public abstract class StaffPageModel(CurrentUser me) : PageModel
{
    public CurrentUser Me => me;

    protected abstract Perm Required { get; }

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!me.Has(Perm.StaffArea) || !me.Has(Required)) context.Result = new NotFoundResult();
    }
}
