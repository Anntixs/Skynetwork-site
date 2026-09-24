using Dapper;

namespace SkyNetwork.Site.Data;

public sealed class FlightPlanService(Database db)
{
    public long File(FlightPlan p)
    {
        using var c = db.Open();
        p.CreatedAt = Database.Now();
        return c.ExecuteScalar<long>("""
            INSERT INTO flight_plans (cid, callsign, rules, aircraft, cruise_speed, departure, destination, alternate,
                departure_time, cruise_altitude, enroute_minutes, fuel_minutes, route, remarks, waypoints, created_at)
            VALUES (@Cid, @Callsign, @Rules, @Aircraft, @CruiseSpeed, @Departure, @Destination, @Alternate,
                @DepartureTime, @CruiseAltitude, @EnrouteMinutes, @FuelMinutes, @Route, @Remarks, @Waypoints, @CreatedAt) RETURNING id
            """, p);
    }

    public FlightPlan? Latest(long cid)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<FlightPlan>("SELECT * FROM flight_plans WHERE cid = @cid ORDER BY id DESC LIMIT 1", new { cid });
    }

    /// <summary>
    /// Route points of the flight a pilot is flying now: their latest plan from the last day with the same
    /// departure and destination, if it came with points (SimBrief import).
    /// </summary>
    public string? Waypoints(long cid, string departure, string destination)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<string>("""
            SELECT waypoints FROM flight_plans
            WHERE cid = @cid AND departure = @departure AND destination = @destination AND created_at > @since AND waypoints <> ''
            ORDER BY id DESC LIMIT 1
            """, new { cid, departure = departure.ToUpperInvariant(), destination = destination.ToUpperInvariant(), since = Database.Now() - 86400 });
    }

    /// <summary>The member's SimBrief username or Pilot ID, remembered from their last import ("" when none).</summary>
    public string SimbriefUser(long cid)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<string>("SELECT simbrief FROM member_profiles WHERE cid = @cid", new { cid }) ?? "";
    }

    public void SetSimbriefUser(long cid, string user)
    {
        using var c = db.Open();
        c.Execute("UPDATE member_profiles SET simbrief = @user WHERE cid = @cid", new { cid, user });
    }

    public IReadOnlyList<FlightPlan> Recent(long cid, int limit = 20)
    {
        using var c = db.Open();
        return c.Query<FlightPlan>("SELECT * FROM flight_plans WHERE cid = @cid ORDER BY id DESC LIMIT @limit", new { cid, limit }).ToList();
    }
}
