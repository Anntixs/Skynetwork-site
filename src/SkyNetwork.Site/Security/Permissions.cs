using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Security;

[Flags]
public enum Perm
{
    None = 0,
    /// <summary>May see the staff area at all (it is a 404 for everyone else).</summary>
    StaffArea = 1 << 0,
    ViewMembers = 1 << 1,
    Suspend = 1 << 2,
    EditRatings = 1 << 3,
    ManageRoles = 1 << 4,
    Tickets = 1 << 5,
    Training = 1 << 6,
    Events = 1 << 7,
    News = 1 << 8,
    Bookings = 1 << 9,
    Audit = 1 << 10,
    Online = 1 << 11,
    Notes = 1 << 12,
    ResetPasswords = 1 << 13,
    /// <summary>Pilot and military ratings (site only).</summary>
    PilotRatings = 1 << 14,
    All = ~0,
}

/// <summary>
/// Who may do what. Network ratings give the base: administrators everything, supervisors run the
/// network day to day, instructors handle training. Administrators can add site roles on top
/// (event manager, news editor, support, training).
/// </summary>
public static class Permissions
{
    public static readonly IReadOnlyDictionary<string, string> Roles = new Dictionary<string, string>
    {
        ["events"] = "Events",
        ["news"] = "News",
        ["support"] = "Support",
        ["training"] = "Training",
    };

    private const Perm Supervisor = Perm.StaffArea | Perm.ViewMembers | Perm.Suspend | Perm.Notes | Perm.Tickets | Perm.Online |
                                    Perm.Bookings | Perm.Audit | Perm.Training | Perm.Events | Perm.News | Perm.PilotRatings;

    private const Perm Instructor = Perm.StaffArea | Perm.ViewMembers | Perm.Notes | Perm.Training | Perm.EditRatings | Perm.Online |
                                    Perm.PilotRatings;

    public static Perm For(int rating, IEnumerable<string> roles)
    {
        if (rating == Ratings.ADM) return Perm.All;
        var p = rating switch
        {
            Ratings.SUP => Supervisor,
            >= Ratings.I1 and <= Ratings.I3 => Instructor,
            _ => Perm.None,
        };
        foreach (var role in roles)
            p |= role switch
            {
                "events" => Perm.StaffArea | Perm.Events,
                "news" => Perm.StaffArea | Perm.News,
                "support" => Perm.StaffArea | Perm.Tickets | Perm.ViewMembers | Perm.ResetPasswords,
                "training" => Instructor,
                _ => Perm.None,
            };
        return p;
    }

    /// <summary>
    /// Whether <paramref name="actorRating"/> may move a member from <paramref name="from"/> to <paramref name="to"/>:
    /// administrators may set anything; everyone else with EditRatings only the controller ladder up to C3,
    /// and never touches staff ratings.
    /// </summary>
    public static bool CanSetRating(int actorRating, Perm actor, int from, int to)
    {
        if (!actor.HasFlag(Perm.EditRatings) || to is < Ratings.OBS or > Ratings.ADM) return false;
        if (actorRating == Ratings.ADM) return true;
        return from <= Ratings.C3 && to <= Ratings.C3;
    }

    /// <summary>
    /// Who may suspend whom: nobody themselves, administrators anyone else, supervisors only members
    /// below supervisor (not other supervisors or administrators).
    /// </summary>
    public static bool CanSuspend(Member actor, Perm actorPerms, Member target) =>
        actorPerms.HasFlag(Perm.Suspend) && actor.Cid != target.Cid &&
        (actor.Rating == Ratings.ADM || target.Rating < Ratings.SUP);
}
