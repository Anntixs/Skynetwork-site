using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SkyNetwork.Site.Localization;

/// <summary>
/// The visitor's language. The site is written in English; <see cref="Ru"/> holds the Russian
/// translations keyed by the English text. The choice lives in the "lang" cookie; English is the default.
/// </summary>
public sealed class Lang
{
    public const string Cookie = "lang";
    public static readonly IReadOnlyList<(string Code, string Name)> Supported = [("en", "English"), ("ru", "Русский")];

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string Code { get; private set; } = "en";
    public bool IsRu => Code == "ru";
    public CultureInfo Culture => IsRu ? RuCulture : En;

    public void Set(string? code) => Code = code == "ru" ? "ru" : "en";

    /// <summary>The text in the visitor's language (the English text itself when there is no translation).</summary>
    public string this[string en] => IsRu && Ru.Texts.TryGetValue(en, out var ru) ? ru : en;

    /// <summary>Translated format string with arguments: <c>L.F("Rating changed to {0}", "S2")</c>.</summary>
    public string F(string en, params object?[] args) => string.Format(Culture, this[en], args);

    /// <summary>"1 pilot", "2 pilots" / "1 пилот", "3 пилота", "5 пилотов" — without the number.</summary>
    public string Plural(int n, string one, string many)
    {
        if (!IsRu) return n == 1 ? one : many;
        if (!Ru.Texts.TryGetValue(one + "|" + many, out var forms)) return n == 1 ? one : many;
        var f = forms.Split('|');
        int i = n % 10 == 1 && n % 100 != 11 ? 0 : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? 1 : 2;
        return f[Math.Min(i, f.Length - 1)];
    }
}

public static class LangExtensions
{
    public static Lang Lang(this HttpContext ctx) => ctx.RequestServices.GetRequiredService<Lang>();

    /// <summary>Translation for page model messages.</summary>
    public static string T(this PageModel page, string en) => page.HttpContext.Lang()[en];
    public static string T(this PageModel page, string en, params object?[] args) => page.HttpContext.Lang().F(en, args);
}
