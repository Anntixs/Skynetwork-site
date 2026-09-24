namespace SkyNetwork.Site.Data;

/// <summary>Controller ratings, numbered as in the FSD server (OBS = 1 … ADM = 12).</summary>
public static class Ratings
{
    public const int OBS = 1, S1 = 2, S2 = 3, S3 = 4, C1 = 5, C2 = 6, C3 = 7, I1 = 8, I2 = 9, I3 = 10, SUP = 11, ADM = 12;

    private static readonly string[] ShortNames = ["?", "OBS", "S1", "S2", "S3", "C1", "C2", "C3", "I1", "I2", "I3", "SUP", "ADM"];

    private static readonly string[] LongNames =
    [
        "?", "Наблюдатель", "Диспетчер DEL/GND", "Диспетчер TWR", "Диспетчер APP", "Диспетчер CTR", "Диспетчер CTR II",
        "Старший диспетчер", "Инструктор", "Инструктор II", "Старший инструктор", "Супервайзер", "Администратор",
    ];

    public static string Short(int rating) => rating is >= OBS and <= ADM ? ShortNames[rating] : "?";
    public static string Long(int rating) => rating is >= OBS and <= ADM ? LongNames[rating] : "?";

    public static int FromShort(string name) => Array.IndexOf(ShortNames, name.Trim().ToUpperInvariant()) is > 0 and var i ? i : 0;

    /// <summary>Ratings a member can train for: the controller ladder up to C3.</summary>
    public static IEnumerable<int> Trainable => [S1, S2, S3, C1, C3];

    /// <summary>The next rating on the training ladder, or null at the top.</summary>
    public static int? Next(int rating) => Trainable.Where(r => r > rating).Cast<int?>().FirstOrDefault();

    public static IEnumerable<int> All => Enumerable.Range(OBS, ADM);
}
