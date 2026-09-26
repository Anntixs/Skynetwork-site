using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>The account letters with links: confirming an email address and resetting the password.</summary>
public sealed class AccountMail(EmailTokenService tokens, Notifications notify, Mailer mailer)
{
    /// <summary>Mail is set up: new members confirm their address, and passwords can be reset by email.</summary>
    public bool Enabled => mailer.Enabled;

    private string Base(HttpRequest r) => mailer.SiteUrl.Length > 0 ? mailer.SiteUrl : $"{r.Scheme}://{r.Host}";

    /// <summary>Sends the confirmation link for <paramref name="email"/>; false when one was sent a moment ago.</summary>
    public bool SendConfirmation(HttpRequest request, Member member, string email)
    {
        if (!Enabled || tokens.Create(member.Cid, EmailTokenService.Verify, email) is not { } token) return false;
        notify.ConfirmEmail(email, member.Name, member.Cid, $"{Base(request)}/verify-email?token={token}");
        return true;
    }

    /// <summary>Sends the password reset link to the member's address; false when there is none or one was sent a moment ago.</summary>
    public bool SendReset(HttpRequest request, Member member)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(member.Email)) return false;
        if (tokens.Create(member.Cid, EmailTokenService.Reset, member.Email) is not { } token) return false;
        notify.ResetPassword(member.Email, member.Name, member.Cid, $"{Base(request)}/reset-password?token={token}");
        return true;
    }
}
