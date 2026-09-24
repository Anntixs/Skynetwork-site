using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class TeamModel(CurrentUser me, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.StaffArea;

    public IReadOnlyList<(Member Member, IReadOnlyList<string> Roles)> Team { get; private set; } = [];

    public void OnGet() => Team = members.Staff().Select(m => (m, members.RolesOf(m.Cid))).ToList();
}
