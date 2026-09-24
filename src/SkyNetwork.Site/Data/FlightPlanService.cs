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
                departure_time, cruise_altitude, enroute_minutes, fuel_minutes, route, remarks, created_at)
            VALUES (@Cid, @Callsign, @Rules, @Aircraft, @CruiseSpeed, @Departure, @Destination, @Alternate,
                @DepartureTime, @CruiseAltitude, @EnrouteMinutes, @FuelMinutes, @Route, @Remarks, @CreatedAt) RETURNING id
            """, p);
    }

    public FlightPlan? Latest(long cid)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<FlightPlan>("SELECT * FROM flight_plans WHERE cid = @cid ORDER BY id DESC LIMIT 1", new { cid });
    }

    public IReadOnlyList<FlightPlan> Recent(long cid, int limit = 20)
    {
        using var c = db.Open();
        return c.Query<FlightPlan>("SELECT * FROM flight_plans WHERE cid = @cid ORDER BY id DESC LIMIT @limit", new { cid, limit }).ToList();
    }
}
