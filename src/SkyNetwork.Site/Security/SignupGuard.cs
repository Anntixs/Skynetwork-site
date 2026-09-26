using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Security;

/// <summary>
/// Keeps junk accounts out of self-registration: a real-looking first and last name, a permanent email address, a form
/// filled in by a person (a hidden field only bots fill in, and a few seconds between showing the form and sending it),
/// and a limit on accounts registered from one address per day. Errors are English texts translated in <c>Ru.cs</c>.
/// </summary>
public sealed class SignupGuard(IDataProtectionProvider protection, IOptions<SiteOptions> options) : IDisposable
{
    public const string RealName = "Enter your real first and last name in letters";
    public const string BadName = "This name cannot be used. Enter your real first and last name";
    public const string BadEmail = "Check the email address";
    public const string TemporaryEmail = "Use a permanent email address, not a temporary one";
    public const string NotAPerson = "The form could not be sent. Please try again";

    private readonly IDataProtector _stamps = protection.CreateProtector("SkyNetwork.Signup.Started");
    private readonly PartitionedRateLimiter<string> _perAddress = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.Value.RegistrationsPerDayPerAddress),
            Window = TimeSpan.FromDays(1),
        }));

    // ---- the form: a hidden field for bots and the moment it was shown --------------------------------------------

    /// <summary>The value of the form's hidden "Started" field: when the form was shown, sealed so it cannot be forged.</summary>
    public string Stamp() => _stamps.Protect(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Null when a person filled in the form: the hidden "Website" field is empty and the form was sent at least
    /// <see cref="SiteOptions.SignupMinSeconds"/> after it was shown (and within a day).
    /// </summary>
    public string? CheckForm(string? website, string? started)
    {
        if (!string.IsNullOrEmpty(website)) return NotAPerson;
        int minSeconds = options.Value.SignupMinSeconds;
        if (minSeconds <= 0) return null;
        try
        {
            long shown = long.Parse(_stamps.Unprotect(started ?? ""), CultureInfo.InvariantCulture);
            double seconds = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - shown) / 1000.0;
            return seconds >= minSeconds && seconds <= 86400 ? null : NotAPerson;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return NotAPerson;
        }
    }

    /// <summary>Takes one of the day's registrations for an address; false when they are used up.</summary>
    public bool TryTakeSlot(string address)
    {
        using var lease = _perAddress.AttemptAcquire(address);
        return lease.IsAcquired;
    }

    public void Dispose() => _perAddress.Dispose();

    // ---- names ----------------------------------------------------------------------------------------------------

    /// <summary>Single spaces; a word typed all in small or all in capital letters gets capitals ("anne-marie" → "Anne-Marie").</summary>
    public static string TidyName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (w.Length > 1 && (w == w.ToLowerInvariant() || w == w.ToUpperInvariant()))
                words[i] = string.Join('-', w.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        }
        return string.Join(' ', words);
    }

    /// <summary>
    /// Null for a name that looks real: two to four words of Latin or Cyrillic letters (one alphabet per word, with an
    /// inner hyphen or apostrophe), each with a vowel, no letter three times in a row, no keyboard runs, no swearing and
    /// no staff titles.
    /// </summary>
    public static string? CheckName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 4 || name.Length > 60) return RealName;
        foreach (string word in words)
        {
            string w = Plain(word);
            if (w.Length is < 2 or > 30 || !IsWord(w) || Repeats(w) || !w.Any(c => Vowels.Contains(c))) return RealName;
            if (KeyboardRuns.Any(w.Contains) || Placeholders.Contains(w)) return RealName;
            string letters = new(w.Where(char.IsLetter).ToArray());
            if (BannedRoots.Any(letters.Contains) || BannedWords.Contains(letters)) return BadName;
        }
        return null;
    }

    // Small letters without accents ("José" → "jose", "Ёлкин" → "елкин").
    private static string Plain(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (char c in word.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsLatin(char c) => c is >= 'a' and <= 'z' or 'ß' or 'æ' or 'ø' or 'œ' or 'ł' or 'đ' or 'ð' or 'þ' or 'ı';
    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ' && char.IsLetter(c);

    // Letters of one alphabet; a hyphen or an apostrophe only between letters.
    private static bool IsWord(string w)
    {
        bool latin = w.Any(IsLatin), cyrillic = w.Any(IsCyrillic);
        if (latin == cyrillic) return false;
        for (int i = 0; i < w.Length; i++)
        {
            char c = w[i];
            if (latin ? IsLatin(c) : IsCyrillic(c)) continue;
            bool joiner = c is '-' or '\'' or '’';
            if (!joiner || i == 0 || i == w.Length - 1 || !char.IsLetter(w[i - 1]) || !char.IsLetter(w[i + 1])) return false;
        }
        return true;
    }

    private static bool Repeats(string w)
    {
        for (int i = 2; i < w.Length; i++)
            if (w[i] == w[i - 1] && w[i] == w[i - 2]) return true;
        return false;
    }

    private const string Vowels = "aeiouyæøœаеиоуыэюяіїєәөұүі";

    // Runs along the keyboard: "qwerty", "asdf", "йцукен", "фыва".
    private static readonly string[] KeyboardRuns = ["qwer", "wert", "asdf", "sdfg", "dfgh", "zxcv", "йцук", "цуке", "фыва", "ывап", "ячсм"];

    // Words that are not a name at all.
    private static readonly HashSet<string> Placeholders =
        ["test", "тест", "name", "имя", "surname", "фамилия", "user", "юзер", "noname", "anonymous", "аноним", "qwerty", "йцукен"];

    // Swearing, slurs and staff titles (Russian also as typed in Latin letters), matched inside a word without accents
    // ("й" is "и" by then). Only roots that are not part of real surnames ("Shitov", "Slutsky", "Pidruchny", "Ebanks",
    // "Niggemann" stay allowed); short words are matched whole below.
    private static readonly string[] BannedRoots =
    [
        "хуе", "хуи", "хую", "хуя", "пизд", "ебан", "ебал", "ебат", "ебуч", "еблан", "ебло", "уеб", "заеб", "наеб", "выеб", "доеб",
        "бляд", "блят", "пидор", "пидар", "педик", "залуп", "гандон", "гондон", "шлюх", "мудак", "мудил", "дроч", "говн", "дебил",
        "ниггер", "нацист",
        "xuy", "xyu", "huil", "huesos", "nahuy", "pohuy", "pizd", "pisd", "yoban", "ebuch", "eblan",
        "blyad", "blyat", "pidor", "pidar", "pederast", "zalup", "gandon", "gondon", "shluh", "shlyuh", "mudak", "mudil", "droch",
        "govno", "debil", "fuck", "cunt", "bitch", "whore", "retard", "penis", "vagina", "porno",
        "администратор", "модератор", "поддержка", "administrator", "moderator", "skynetwork",
    ];

    private static readonly HashSet<string> BannedWords =
    [
        "сука", "суки", "жопа", "чмо", "лох", "гитлер", "админ",
        "suka", "zhopa", "jopa", "chmo", "shit", "slut", "fag", "faggot", "nigger", "nigga", "ass", "sex", "nazi", "hitler", "rape",
        "admin", "support", "staff", "system",
    ];

    // ---- email ----------------------------------------------------------------------------------------------------

    /// <summary>Null for a usable address: one mailbox, a domain with a proper ending, not a throwaway mail service.</summary>
    public static string? CheckEmail(string email)
    {
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email || address.DisplayName.Length > 0) return BadEmail;
        string host = address.Host.ToLowerInvariant();
        int dot = host.LastIndexOf('.');
        if (dot <= 0 || host.Length - dot - 1 < 2 || !host[(dot + 1)..].All(char.IsAsciiLetter) || host.Contains("..")) return BadEmail;
        if (Placeholder.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal))) return BadEmail;
        if (TemporaryMail.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal))) return TemporaryEmail;
        return null;
    }

    // Domains from address templates, not anyone's mailbox ("62bcc4f00c0b@yourdomain.com" was a bot wave).
    private static readonly string[] Placeholder =
        ["yourdomain.com", "yourdomain.net", "yourdomain.org", "yourdomain.ru", "mydomain.com", "domain.com", "domain.ru", "test.com", "test.ru", "sample.com"];

    // Throwaway mailbox services.
    private static readonly string[] TemporaryMail =
    [
        "mailinator.com", "guerrillamail.com", "guerrillamail.net", "guerrillamail.org", "sharklasers.com", "grr.la", "10minutemail.com",
        "10minutemail.net", "temp-mail.org", "temp-mail.io", "tempmail.com", "tempmail.net", "tempmail.dev", "tempmailo.com", "tmpmail.org",
        "tmpmail.net", "yopmail.com", "yopmail.net", "getnada.com", "nada.email", "dispostable.com", "maildrop.cc", "mailnesia.com",
        "trashmail.com", "trashmail.net", "fakeinbox.com", "throwawaymail.com", "mohmal.com", "emailondeck.com", "mintemail.com",
        "mailcatch.com", "1secmail.com", "1secmail.net", "1secmail.org", "moakt.com", "tempr.email", "discard.email", "mytemp.email",
        "burnermail.io", "mail.tm", "dropmail.me", "10mail.org", "emltmp.com", "mailforspam.com", "spam4.me", "crazymailing.com",
    ];

    // ---- accounts already registered ------------------------------------------------------------------------------

    /// <summary>
    /// Why an existing account looks like a junk registration (for the staff list), or null. The team and members given a
    /// rating have been looked at by a person already.
    /// </summary>
    public static string? Suspicion(Member m) =>
        m.StaffRank > 0 || m.Rating > Ratings.OBS ? null : CheckName(m.Name) ?? (m.Email is { Length: > 0 } email ? CheckEmail(email) : null);
}
