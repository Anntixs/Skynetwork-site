using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace SkyNetwork.Site.Data;

/// <summary>
/// One-time links sent by email: "verify" confirms an address (also a new one when the email is changed),
/// "reset" sets a new password. Only a hash of the link is stored.
/// </summary>
public sealed class EmailTokenService(Database db)
{
    public const string Verify = "verify", Reset = "reset";
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(48), ResetLifetime = TimeSpan.FromHours(1);
    /// <summary>At most one letter of a kind per member in this time.</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(2);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A new link for the member, or null when one was sent too recently.</summary>
    public string? Create(long cid, string purpose, string email)
    {
        long now = Database.Now();
        using var c = db.Open();
        long? last = c.ExecuteScalar<long?>("SELECT MAX(created_at) FROM email_tokens WHERE cid = @cid AND purpose = @purpose", new { cid, purpose });
        if (last is { } l && now - l < ResendInterval.TotalSeconds) return null;
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var lifetime = purpose == Reset ? ResetLifetime : VerifyLifetime;
        // Only the newest link of a kind works.
        c.Execute("UPDATE email_tokens SET used = 1 WHERE cid = @cid AND purpose = @purpose", new { cid, purpose });
        c.Execute("DELETE FROM email_tokens WHERE expires_at < @now", new { now });
        c.Execute("""
            INSERT INTO email_tokens (token_hash, cid, purpose, email, created_at, expires_at) VALUES (@hash, @cid, @purpose, @email, @now, @expires)
            """, new { hash = Hash(token), cid, purpose, email, now, expires = now + (long)lifetime.TotalSeconds });
        return token;
    }

    private sealed class Row
    {
        public long Cid { get; set; }
        public string Email { get; set; } = "";
    }

    /// <summary>Whose link it is (and the address it was sent to) while it is valid and unused; <paramref name="consume"/> uses it up.</summary>
    public (long Cid, string Email)? Check(string? token, string purpose, bool consume)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<Row>("""
            SELECT cid, email FROM email_tokens WHERE token_hash = @hash AND purpose = @purpose AND used = 0 AND expires_at > @now
            """, new { hash = Hash(token.Trim()), purpose, now = Database.Now() });
        if (row == null) return null;
        // Two clicks at once: only one of them uses the link.
        if (consume && c.Execute("UPDATE email_tokens SET used = 1 WHERE token_hash = @hash AND used = 0", new { hash = Hash(token.Trim()) }) != 1)
            return null;
        return (row.Cid, row.Email);
    }
}
