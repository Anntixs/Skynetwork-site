using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace SkyNetwork.Site.Data;

public sealed class ConnectClient
{
    public long Id { get; set; }
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string SecretHint { get; set; } = "";
    /// <summary>Allowed redirect addresses, one per line; the one in a request must match exactly.</summary>
    public string RedirectUris { get; set; } = "";
    public bool Active { get; set; }
    public long CreatedAt { get; set; }

    public IReadOnlyList<string> Redirects => RedirectUris.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public DateTime Created => Time.Utc(CreatedAt);
}

public sealed class ConnectConsent
{
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "";
    public long CreatedAt { get; set; }
    public DateTime Created => Time.Utc(CreatedAt);
}

/// <summary>
/// SkyNetwork Connect: other sites (division sites and the like) sign members in through the network
/// with the OAuth 2.0 authorization code flow (PKCE supported). The member enters the password on the
/// network site only; the other site receives a one-time code, exchanges it for an access token with
/// its client secret, and reads the member's profile.
/// </summary>
public sealed class ConnectService(Database db, AuditService audit)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    /// <summary>profile: CID, name and ratings; email: the e-mail address.</summary>
    public static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["profile"] = "CID, name and ratings",
        ["email"] = "E-mail address",
    };

    // ---- clients (administrators) ---------------------------------------------------------

    public IReadOnlyList<ConnectClient> Clients()
    {
        using var c = db.Open();
        return c.Query<ConnectClient>("SELECT * FROM oauth_clients ORDER BY name").ToList();
    }

    public ConnectClient? Client(string clientId)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<ConnectClient>("SELECT * FROM oauth_clients WHERE client_id = @clientId", new { clientId });
    }

    public ConnectClient? Client(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<ConnectClient>("SELECT * FROM oauth_clients WHERE id = @id", new { id });
    }

    /// <summary>Redirect addresses must be absolute https (http only for localhost, for development).</summary>
    public static string? ValidateRedirects(string text, out string normalized)
    {
        var list = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        normalized = string.Join("\n", list);
        if (list.Count == 0) return "Add at least one redirect address";
        foreach (var u in list)
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0) return "Redirect addresses must be full addresses without #";
            bool local = uri.IsLoopback;
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && local)) return "Redirect addresses must use https:// (http:// only for localhost)";
        }
        return null;
    }

    public (string? ClientId, string? Secret, string? Error) CreateClient(long actor, string name, string redirects)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 80) return (null, null, "The name is 2 to 80 characters");
        if (ValidateRedirects(redirects, out var normalized) is { } error) return (null, null, error);
        string clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        string secret = NewSecret();
        using var c = db.Open();
        c.Execute("""
            INSERT INTO oauth_clients (client_id, name, secret_hash, secret_hint, redirect_uris, created_by, created_at)
            VALUES (@clientId, @name, @hash, @hint, @normalized, @actor, @now)
            """, new { clientId, name, hash = Hash(secret), hint = secret[..8] + "…", normalized, actor, now = Database.Now() });
        audit.Log(actor, "connect-client", clientId, $"{name}: created");
        return (clientId, secret, null);
    }

    public string? UpdateClient(long actor, long id, string name, string redirects, bool active)
    {
        var client = Client(id);
        if (client == null) return "Site not found";
        name = name.Trim();
        if (name.Length is < 2 or > 80) return "The name is 2 to 80 characters";
        if (ValidateRedirects(redirects, out var normalized) is { } error) return error;
        using var c = db.Open();
        c.Execute("UPDATE oauth_clients SET name = @name, redirect_uris = @normalized, active = @active WHERE id = @id",
            new { id, name, normalized, active });
        if (!active) c.Execute("DELETE FROM oauth_tokens WHERE client_id = @ClientId", new { client.ClientId });
        audit.Log(actor, "connect-client", client.ClientId, $"{name}: {(active ? "updated" : "disabled")}");
        return null;
    }

    public string? NewClientSecret(long actor, long id)
    {
        var client = Client(id);
        if (client == null) return null;
        string secret = NewSecret();
        using var c = db.Open();
        c.Execute("UPDATE oauth_clients SET secret_hash = @hash, secret_hint = @hint WHERE id = @id",
            new { id, hash = Hash(secret), hint = secret[..8] + "…" });
        audit.Log(actor, "connect-client", client.ClientId, $"{client.Name}: new secret");
        return secret;
    }

    // ---- authorization ----------------------------------------------------------------------

    /// <summary>The requested scopes, or null when one is unknown. Empty means "profile".</summary>
    public static IReadOnlyList<string>? ParseScope(string? scope)
    {
        var list = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        if (list.Any(s => !Scopes.ContainsKey(s))) return null;
        if (!list.Contains("profile")) list.Insert(0, "profile");
        return list;
    }

    public bool HasConsent(long cid, string clientId, IReadOnlyList<string> scopes)
    {
        using var c = db.Open();
        var granted = c.QuerySingleOrDefault<string>("SELECT scope FROM oauth_consents WHERE cid = @cid AND client_id = @clientId", new { cid, clientId });
        return granted != null && scopes.All(s => granted.Split(' ').Contains(s));
    }

    public void SaveConsent(long cid, string clientId, IReadOnlyList<string> scopes)
    {
        using var c = db.Open();
        c.Execute("""
            INSERT INTO oauth_consents (cid, client_id, scope, created_at) VALUES (@cid, @clientId, @scope, @now)
            ON CONFLICT(cid, client_id) DO UPDATE SET scope = @scope
            """, new { cid, clientId, scope = string.Join(' ', scopes), now = Database.Now() });
    }

    public IReadOnlyList<ConnectConsent> Consents(long cid)
    {
        using var c = db.Open();
        return c.Query<ConnectConsent>("""
            SELECT k.client_id, k.scope, k.created_at, COALESCE(o.name, k.client_id) AS name
            FROM oauth_consents k LEFT JOIN oauth_clients o ON o.client_id = k.client_id WHERE k.cid = @cid ORDER BY name
            """, new { cid }).ToList();
    }

    /// <summary>The member withdraws a site's access: the consent and its tokens go.</summary>
    public void Revoke(long cid, string clientId)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM oauth_consents WHERE cid = @cid AND client_id = @clientId", new { cid, clientId });
        c.Execute("DELETE FROM oauth_tokens WHERE cid = @cid AND client_id = @clientId", new { cid, clientId });
    }

    public string IssueCode(string clientId, long cid, string redirectUri, IReadOnlyList<string> scopes, string codeChallenge)
    {
        string code = Random();
        using var c = db.Open();
        long now = Database.Now();
        c.Execute("DELETE FROM oauth_codes WHERE expires_at < @now", new { now });
        c.Execute("""
            INSERT INTO oauth_codes (code_hash, client_id, cid, redirect_uri, scope, code_challenge, expires_at)
            VALUES (@hash, @clientId, @cid, @redirectUri, @scope, @codeChallenge, @expires)
            """, new { hash = Hash(code), clientId, cid, redirectUri, scope = string.Join(' ', scopes), codeChallenge,
                       expires = now + (long)CodeLifetime.TotalSeconds });
        return code;
    }

    public sealed record TokenResult(string? AccessToken, string Scope, string? Error, string? Description);

    private sealed class CodeRow
    {
        public string ClientId { get; set; } = "";
        public long Cid { get; set; }
        public string RedirectUri { get; set; } = "";
        public string Scope { get; set; } = "";
        public string CodeChallenge { get; set; } = "";
        public long ExpiresAt { get; set; }
        public long Used { get; set; }
    }

    /// <summary>The token endpoint: a one-time code for an access token (errors as in RFC 6749).</summary>
    public TokenResult Exchange(string clientId, string clientSecret, string code, string redirectUri, string? codeVerifier)
    {
        var client = Client(clientId);
        if (client is not { Active: true } || !FixedEquals(client.SecretHash, Hash(clientSecret)))
            return new(null, "", "invalid_client", "Unknown client or wrong secret");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var row = c.QuerySingleOrDefault<CodeRow>("SELECT * FROM oauth_codes WHERE code_hash = @hash", new { hash = Hash(code) }, tx);
        if (row == null || row.ClientId != clientId || row.Used != 0 || row.ExpiresAt < Database.Now() || row.RedirectUri != redirectUri)
            return new(null, "", "invalid_grant", "The code is invalid, expired, already used or for another address");
        if (row.CodeChallenge.Length > 0 && (codeVerifier == null || !FixedEquals(row.CodeChallenge, Challenge(codeVerifier))))
            return new(null, "", "invalid_grant", "code_verifier does not match");
        c.Execute("UPDATE oauth_codes SET used = 1 WHERE code_hash = @hash", new { hash = Hash(code) }, tx);
        string token = Random();
        long now = Database.Now();
        c.Execute("DELETE FROM oauth_tokens WHERE expires_at < @now", new { now }, tx);
        c.Execute("INSERT INTO oauth_tokens (token_hash, client_id, cid, scope, expires_at) VALUES (@hash, @clientId, @Cid, @Scope, @expires)",
            new { hash = Hash(token), clientId, row.Cid, row.Scope, expires = now + (long)TokenLifetime.TotalSeconds }, tx);
        tx.Commit();
        return new(token, row.Scope, null, null);
    }

    /// <summary>The member and scopes behind an access token, or null.</summary>
    public (long Cid, string[] Scopes)? Token(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 200) return null;
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<TokenRow>("""
            SELECT t.cid, t.scope FROM oauth_tokens t JOIN oauth_clients o ON o.client_id = t.client_id AND o.active = 1
            WHERE t.token_hash = @hash AND t.expires_at >= @now
            """, new { hash = Hash(accessToken.Trim()), now = Database.Now() });
        return row == null ? null : (row.Cid, row.Scope.Split(' '));
    }

    private sealed class TokenRow
    {
        public long Cid { get; set; }
        public string Scope { get; set; } = "";
    }

    /// <summary>PKCE S256: base64url(SHA-256(verifier)).</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string NewSecret() => "sks_" + Base64Url(RandomNumberGenerator.GetBytes(30));
    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
