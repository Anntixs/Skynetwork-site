namespace SkyNetwork.Site.Data;

/// <summary>Controller ratings, numbered as in the FSD server (OBS = 1 … ADM = 12).</summary>
public static class Ratings
{
    public const int OBS = 1, S1 = 2, S2 = 3, S3 = 4, C1 = 5, C2 = 6, C3 = 7, I1 = 8, I2 = 9, I3 = 10, SUP = 11, ADM = 12;

    private static readonly string[] ShortNames = ["?", "OBS", "S1", "S2", "S3", "C1", "C2", "C3", "I1", "I2", "I3", "SUP", "ADM"];

    // English names; pages translate them.
    private static readonly string[] LongNames =
    [
        "?", "Observer", "Delivery/Ground Controller", "Tower Controller", "Approach Controller", "Enroute Controller",
        "Senior Enroute Controller", "Senior Controller", "Instructor", "Senior Instructor", "Chief Instructor",
        "Supervisor", "Administrator",
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

/// <summary>A rating ladder shown on profiles: pilot or military ratings (site only, the FSD server does not use them).</summary>
public sealed class RatingLadder(string kind, (string Short, string Long, string Privileges)[] levels)
{
    public string Kind { get; } = kind;
    public IReadOnlyList<(string Short, string Long, string Privileges)> Levels { get; } = levels;

    public string Short(int level) => level >= 0 && level < Levels.Count ? Levels[level].Short : "?";
    public string Long(int level) => level >= 0 && level < Levels.Count ? Levels[level].Long : "?";
    public bool Valid(int level) => level >= 0 && level < Levels.Count;
    public IEnumerable<int> All => Enumerable.Range(0, Levels.Count);
}

public static class PilotRatings
{
    public static readonly RatingLadder Pilot = new("pilot",
    [
        ("P0", "No pilot rating", "Fly on the network: every member starts here"),
        ("PPL", "Private Pilot License", "VFR flying, radio phraseology, airport procedures"),
        ("IR", "Instrument Rating", "IFR flying: SIDs, STARs, holds and instrument approaches"),
        ("CMEL", "Commercial Multi-Engine License", "Multi-engine aircraft, abnormal procedures, crew operations"),
        ("ATPL", "Airline Transport Pilot License", "Airline operations: long-haul, oceanic, high traffic"),
        ("FI", "Flight Instructor", "Trains pilots for the ratings above"),
        ("FE", "Flight Examiner", "Holds pilot rating checkrides"),
    ]);

    public static readonly RatingLadder Military = new("military",
    [
        ("M0", "No military rating", "Civil flying only"),
        ("M1", "Military Pilot License", "Military aircraft in VFR, formation basics"),
        ("M2", "Military Instrument Rating", "Military IFR, tactical approaches and recoveries"),
        ("M3", "Military Multi-Engine Rating", "Transport and tanker aircraft, air-to-air refuelling"),
        ("M4", "Military Mission Ready Pilot", "Mission flying in military exercises and events"),
    ]);
}

/// <summary>Training tracks: controller ratings, pilot ratings and military ratings.</summary>
public static class TrainingTracks
{
    public static readonly IReadOnlyList<(string Key, string Title)> All =
        [("atc", "Air traffic control"), ("pilot", "Pilot ratings"), ("military", "Military ratings")];

    public static bool Valid(string track) => track is "atc" or "pilot" or "military";

    public static string Short(string track, int level) => track switch
    {
        "pilot" => PilotRatings.Pilot.Short(level),
        "military" => PilotRatings.Military.Short(level),
        _ => Ratings.Short(level),
    };

    public static string Long(string track, int level) => track switch
    {
        "pilot" => PilotRatings.Pilot.Long(level),
        "military" => PilotRatings.Military.Long(level),
        _ => Ratings.Long(level),
    };

    /// <summary>
    /// The next level a member can train for, or null. Pilot training goes up to ATPL and military to
    /// M4; instructor and examiner ratings are given by staff, not requested.
    /// </summary>
    public static int? Next(string track, int current) => track switch
    {
        "pilot" => current < 4 ? current + 1 : null,
        "military" => current < 4 ? current + 1 : null,
        _ => Ratings.Next(current),
    };

    public static int Current(string track, Member m) => track switch
    {
        "pilot" => m.PilotRating,
        "military" => m.MilitaryRating,
        _ => m.Rating,
    };
}
