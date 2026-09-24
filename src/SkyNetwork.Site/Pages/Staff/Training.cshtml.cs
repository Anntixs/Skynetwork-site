using SkyNetwork.Site.Data;
using SkyNetwork.Site.Localization;
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
                bool allowed = r.Cid != Me.Cid && (r.Track == "atc"
                    ? Permissions.CanSetRating(Me.Member!.Rating, Me.Permissions, r.Rating, r.TargetRating)
                    : Me.Has(Perm.PilotRatings));
                if (!allowed) Error = "You cannot grant this rating";
                else if (members.Find(r.Cid) is { } m)
                {
                    if (r.Track == "atc") members.SetRating(Me.Cid, r.Cid, r.TargetRating);
                    else members.SetPilotRatings(Me.Cid, r.Cid,
                        r.Track == "pilot" ? r.TargetRating : m.PilotRating,
                        r.Track == "military" ? r.TargetRating : m.MilitaryRating);
                    status = "completed";
                    Message = this.T("{0}: {1} granted", r.Name, r.TargetShort);
                }
            }
            if (Error == null)
            {
                support.UpdateTraining(Me.Cid, id, status, (comment ?? "").Trim());
                Message ??= "Request updated";
            }
        }
        OnGet(null);
    }
}
