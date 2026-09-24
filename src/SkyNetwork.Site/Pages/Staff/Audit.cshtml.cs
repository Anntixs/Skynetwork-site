using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class AuditModel(CurrentUser me, AuditService audit) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Audit;

    public IReadOnlyList<AuditEntry> Entries { get; private set; } = [];

    public void OnGet() => Entries = audit.Recent(500);
}
