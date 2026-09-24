using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class MembersModel(CurrentUser me, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ViewMembers;

    public string Query { get; private set; } = "";
    public bool SuspendedOnly { get; private set; }
    public IReadOnlyList<Member> Results { get; private set; } = [];

    public void OnGet(string? q, int? suspended)
    {
        Query = q ?? "";
        SuspendedOnly = suspended == 1;
        Results = members.Search(q, SuspendedOnly);
    }
}
