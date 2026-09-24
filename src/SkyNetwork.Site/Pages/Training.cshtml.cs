using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

public sealed class TrainingModel(CurrentUser me, SupportService support) : PageModel
{
    public IReadOnlyList<TrainingRequest> Requests { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet() => Requests = support.Training(me.Cid);

    public IActionResult OnPost(int target, string? text)
    {
        Error = support.RequestTraining(me.Cid, me.Member!.Rating, target, (text ?? "").Trim());
        if (Error == null) Message = "Заявка подана. Инструктор ответит здесь";
        OnGet();
        return Page();
    }
}
