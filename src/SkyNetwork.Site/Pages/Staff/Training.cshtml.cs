using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages.Staff;

public sealed class TrainingModel(CurrentUser me, SupportService support, MemberService members) : StaffPageModel(me)
{
    protected override Perm Required => Perm.Training;

    public bool All { get; private set; }
    public IReadOnlyList<TrainingRequest> List { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet(int? all)
    {
        All = all == 1;
        List = support.Training(activeOnly: !All);
    }

    public void OnPost(long id, string status, string? comment, bool promote)
    {
        if (support.TrainingRequest(id) is { } r)
        {
            if (promote)
            {
                // Promotion completes the request; only allowed within the actor's rating powers.
                if (r.Cid == Me.Cid || !Permissions.CanSetRating(Me.Member!.Rating, Me.Permissions, r.Rating, r.TargetRating))
                    Error = "Присвоить этот рейтинг вы не можете";
                else
                {
                    members.SetRating(Me.Cid, r.Cid, r.TargetRating);
                    status = "completed";
                    Message = $"{r.Name}: присвоен {Ratings.Short(r.TargetRating)}";
                }
            }
            if (Error == null)
            {
                support.UpdateTraining(Me.Cid, id, status, (comment ?? "").Trim());
                Message ??= "Заявка обновлена";
            }
        }
        OnGet(null);
    }
}
