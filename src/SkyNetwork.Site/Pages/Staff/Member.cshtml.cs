using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class MemberModel(CurrentUser me, MemberService members, SessionService sessions, AuditService audit) : StaffPageModel(me)
{
    protected override Perm Required => Perm.ViewMembers;

    public Member Member { get; private set; } = new();
    public MemberHours Hours { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<NetworkSession> Sessions { get; private set; } = [];
    public IReadOnlyList<StaffNote> Notes { get; private set; } = [];
    public IReadOnlyList<AuditEntry> History { get; private set; } = [];
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public IReadOnlyList<int> RatingOptions { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    private bool Load(long cid)
    {
        if (members.Find(cid) is not { } m) return false;
        Member = m;
        Hours = sessions.Hours(cid);
        Sessions = sessions.Recent(cid, 30);
        Roles = members.RolesOf(cid);
        if (Me.Has(Perm.Notes)) Notes = members.Notes(cid);
        if (Me.Has(Perm.Audit)) History = audit.Recent(50, cid.ToString());
        RatingOptions = m.Cid == Me.Cid ? [] // nobody changes their own rating
            : Ratings.All.Where(r => r == m.Rating || Permissions.CanSetRating(Me.Member!.Rating, Me.Permissions, m.Rating, r)).ToList();
        if (RatingOptions.Count == 1) RatingOptions = [];
        return true;
    }

    public IActionResult OnGet(long cid) => Load(cid) ? Page() : NotFound();

    public IActionResult OnPostRating(long cid, int rating)
    {
        if (!Load(cid)) return NotFound();
        if (cid == Me.Cid || !Permissions.CanSetRating(Me.Member!.Rating, Me.Permissions, Member.Rating, rating)) Error = "Этот рейтинг вам выставлять нельзя";
        else if (rating != Member.Rating)
        {
            members.SetRating(Me.Cid, cid, rating);
            Message = $"Рейтинг изменён на {Ratings.Short(rating)}";
        }
        Load(cid);
        return Page();
    }

    public IActionResult OnPostSuspend(long cid, bool suspend, string? reason)
    {
        if (!Load(cid)) return NotFound();
        if (!Me.Has(Perm.Suspend) || cid == Me.Cid) return NotFound();
        // Supervisors cannot lock out administrators.
        if (Member.Rating == Ratings.ADM && Me.Member!.Rating != Ratings.ADM) Error = "Администратора может заблокировать только администратор";
        else if (suspend && string.IsNullOrWhiteSpace(reason)) Error = "Укажите причину";
        else
        {
            members.SetSuspended(Me.Cid, cid, suspend, (reason ?? "").Trim());
            Message = suspend ? "Участник заблокирован" : "Участник разблокирован";
        }
        Load(cid);
        return Page();
    }

    public IActionResult OnPostResetPassword(long cid)
    {
        if (!Load(cid) || !Me.Has(Perm.ResetPasswords)) return NotFound();
        string temp = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).Replace('+', 'x').Replace('/', 'y');
        members.ResetPassword(Me.Cid, cid, temp);
        Message = $"Временный пароль: {temp} — он показан один раз";
        return Page();
    }

    public IActionResult OnPostRoles(long cid, string[] roles)
    {
        if (!Load(cid) || !Me.Has(Perm.ManageRoles)) return NotFound();
        members.SetRoles(Me.Cid, cid, roles);
        Message = "Роли сохранены";
        Load(cid);
        return Page();
    }

    public IActionResult OnPostNote(long cid, string body)
    {
        if (!Load(cid) || !Me.Has(Perm.Notes)) return NotFound();
        if (!string.IsNullOrWhiteSpace(body)) members.AddNote(Me.Cid, cid, body.Trim());
        return Redirect($"/staff/members/{cid}");
    }
}
