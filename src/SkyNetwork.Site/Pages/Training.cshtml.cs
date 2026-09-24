using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

public sealed class TrainingModel(CurrentUser me, SupportService support, DivisionService divisions) : PageModel
{
    public Division? Division { get; private set; }
    public IReadOnlyList<Division> Divisions { get; private set; } = [];
    public IReadOnlyList<TrainingRequest> Requests { get; private set; } = [];
    public IReadOnlyList<RatingRequest> RatingRequests { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        Division = divisions.Of(me.Cid);
        Divisions = divisions.All(activeOnly: true);
        Requests = support.Training(me.Cid);
        RatingRequests = divisions.Requests(cid: me.Cid);
    }

    public IActionResult OnPost(string track, int target, string? text)
    {
        track = TrainingTracks.Valid(track) ? track : "atc";
        Error = support.RequestTraining(me.Cid, divisions.Of(me.Cid)?.Id, track, TrainingTracks.Current(track, me.Member!), target, (text ?? "").Trim());
        if (Error == null) Message = "Application sent to your division's academy";
        OnGet();
        return Page();
    }

    public IActionResult OnPostDivision(long? divisionId)
    {
        Error = divisions.SetMemberDivision(me.Cid, me.Cid, divisionId is > 0 ? divisionId : null);
        if (Error == null) Message = "Division saved";
        OnGet();
        return Page();
    }
}
