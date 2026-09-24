using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class NewsModel(CurrentUser me, ContentService content) : StaffPageModel(me)
{
    protected override Perm Required => Perm.News;

    public IReadOnlyList<NewsPost> List { get; private set; } = [];

    public void OnGet() => List = content.News(200, includeUnpublished: true);
}
