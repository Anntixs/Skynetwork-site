using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Pages;

/// <summary>Contact form for people who cannot sign in; members use /account/support.</summary>
[EnableRateLimiting("auth")]
public sealed class SupportModel(SupportService support) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Cid { get; set; } = "";
    [BindProperty] public string Subject { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    public bool Sent { get; private set; }
    public string? Error { get; private set; }

    public IActionResult OnGet(int? sent)
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/account/support");
        Sent = sent == 1;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!MailAddress.TryCreate(Email.Trim(), out _)) Error = "Check the email address";
        else if (Subject.Trim().Length < 3 || Body.Trim().Length < 10) Error = "Please describe your question in more detail";
        if (Error != null) return Page();
        string body = Body.Trim() + (Cid.Trim().Length > 0 ? $"\n\nCID: {Cid.Trim()}" : "");
        support.OpenTicket(null, Email.Trim(), Subject.Trim(), body);
        return Redirect("/support?sent=1");
    }
}
