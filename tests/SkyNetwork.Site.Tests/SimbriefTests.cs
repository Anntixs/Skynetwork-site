using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Tests;

public class SimbriefTests
{
    // The parts of a SimBrief OFP (json=v2) the import reads.
    private const string Ofp = """
        {"fetch":{"userid":"123456","status":"Success"},
         "general":{"icao_airline":"AFL","flight_number":"1234","cruise_tas":"447","initial_altitude":"35000",
                    "route":"ARTIM  UL603 NEVEM DCT GOLSA"},
         "origin":{"icao_code":"UUEE","pos_lat":"55.972642","pos_long":"37.414589"},
         "destination":{"icao_code":"EDDF","pos_lat":"50.033306","pos_long":"8.570456"},
         "alternate":[{"icao_code":"EDDK"},{"icao_code":"EDDL"}],
         "aircraft":{"icao_code":"A20N"},
         "atc":{"callsign":"AFL1234"},
         "times":{"sched_out":"1790251200","est_time_enroute":"13500","endurance":"19800"},
         "navlog":{"fix":[
            {"ident":"ARTIM","type":"wpt","pos_lat":"55.931","pos_long":"36.915"},
            {"ident":"NEVEM","type":"wpt","pos_lat":"54.1","pos_long":"28.2"},
            {"ident":"GOLSA","type":"wpt","pos_lat":"51.2","pos_long":"12.1"},
            {"ident":"EDDF","type":"apt","pos_lat":"50.033306","pos_long":"8.570456"}]}}
        """;

    [Fact]
    public void ReadsTheLatestPlan_WithRoutePoints()
    {
        var (plan, error) = Simbrief.Parse(Ofp);
        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.Equal(("AFL1234", "A20N", 447), (plan.Callsign, plan.Aircraft, plan.CruiseSpeed));
        Assert.Equal(("UUEE", "EDDF", "EDDK"), (plan.Departure, plan.Destination, plan.Alternate));
        Assert.Equal(("1200", "FL350", 225, 330), (plan.DepartureTime, plan.CruiseAltitude, plan.EnrouteMinutes, plan.FuelMinutes));
        Assert.Equal("ARTIM UL603 NEVEM DCT GOLSA", plan.Route);

        // Departure first, destination once at the end (the navlog already ends with it).
        var points = JsonDocument.Parse(plan.Waypoints).RootElement.EnumerateArray().Select(p => p[0].GetString()).ToList();
        Assert.Equal(["UUEE", "ARTIM", "NEVEM", "GOLSA", "EDDF"], points);
    }

    [Fact]
    public void ExplainsWhatWentWrong()
    {
        var (plan, error) = Simbrief.Parse("""{"fetch":{"userid":"","status":"Error: Unknown UserID"}}""");
        Assert.Null(plan);
        Assert.Contains("no such SimBrief user", error);
        Assert.NotNull(Simbrief.Parse("<html>").Error);
        Assert.NotNull(Simbrief.Parse("[]").Error);
    }

    [Fact]
    public void KeepsOnlyWellFormedPointsFromTheForm()
    {
        Assert.Equal("""[["UUEE",55.9726,37.4146],["ULLI",59.8,30.26]]""", Simbrief.Clean("""[["uuee",55.972642,37.414589],["ulli",59.8,30.26]]"""));
        Assert.Equal("", Simbrief.Clean("""[["UUEE",95,37]]"""));
        Assert.Equal("", Simbrief.Clean("""{"a":1}"""));
        Assert.Equal("", Simbrief.Clean("""[["UUEE","x",1],["B",1,1]]"""));
        Assert.Equal("", Simbrief.Clean("not json"));
        Assert.Equal("", Simbrief.Clean(null));
    }

    [Fact]
    public async Task MapGetsTheFiledRoutePoints_AndTheFlownTrack()
    {
        using var site = new SiteFactory();
        long cid = site.Member("Route Pilot");
        var c = site.Browser();
        await c.LoginAsync(cid);
        var r = await c.SubmitAsync("/flightplan", new Dictionary<string, string>
        {
            ["Plan.Callsign"] = "AFL1234", ["Plan.Rules"] = "IFR", ["Plan.Aircraft"] = "A20N", ["Plan.CruiseSpeed"] = "447",
            ["Plan.Departure"] = "UUEE", ["Plan.Destination"] = "EDDF", ["Plan.Alternate"] = "", ["Plan.DepartureTime"] = "0920",
            ["Plan.CruiseAltitude"] = "FL350", ["Plan.EnrouteMinutes"] = "225", ["Plan.FuelMinutes"] = "330",
            ["Plan.Route"] = "ARTIM UL603 NEVEM", ["Plan.Remarks"] = "/V/",
            ["Plan.Waypoints"] = """[["UUEE",55.97,37.41],["ARTIM",55.93,36.91],["EDDF",50.03,8.57]]""",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var feed = site.Get<NetworkFeed>();
        string Feed(double lat, int alt) => $$"""
            {"general":{"server":"SkyNetwork","update_timestamp":1790000000},
             "pilots":[{"cid":{{cid}},"name":"Route Pilot","callsign":"AFL1234","logon_time":1789999000,"latitude":{{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"longitude":37.3,
                        "altitude":{{alt}},"groundspeed":280,"transponder":"2000","flight_plan":"*A:I:A20N:447:UUEE:0920:0:FL350:EDDF:3:45:5:30::/V/:ARTIM UL603 NEVEM"}],
             "controllers":[]}
            """;
        feed.Ingest(Feed(55.9, 4500));
        feed.Ingest(Feed(55.9, 4600));   // no real change: not a new point
        feed.Ingest(Feed(55.8, 6000));

        var route = await c.GetFromJsonAsync<JsonElement>("/api/v1/pilots/afl1234/route");
        Assert.Equal(["UUEE", "ARTIM", "EDDF"], route.GetProperty("waypoints").EnumerateArray().Select(p => p[0].GetString()));
        var track = route.GetProperty("track").EnumerateArray().ToList();
        Assert.Equal(2, track.Count);
        Assert.Equal((55.8, 6000), (track[1][0].GetDouble(), track[1][2].GetInt32()));

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/pilots/NOBODY/route")).StatusCode);
        feed.Ingest("""{"general":{"server":"SkyNetwork","update_timestamp":1790000030},"pilots":[],"controllers":[]}""");
        Assert.Empty(feed.Track("AFL1234"));
    }

    [Fact]
    public void OlderDatabasesGetTheRoutePointsColumn()
    {
        using var site = new SiteFactory();
        using (var old = new SqliteConnection($"Data Source={site.DatabasePath}"))
        {
            old.Execute("""
                CREATE TABLE flight_plans (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, cid INTEGER NOT NULL, callsign TEXT NOT NULL,
                    rules TEXT NOT NULL, aircraft TEXT NOT NULL, cruise_speed INTEGER NOT NULL,
                    departure TEXT NOT NULL, destination TEXT NOT NULL, alternate TEXT NOT NULL DEFAULT '',
                    departure_time TEXT NOT NULL, cruise_altitude TEXT NOT NULL,
                    enroute_minutes INTEGER NOT NULL, fuel_minutes INTEGER NOT NULL,
                    route TEXT NOT NULL, remarks TEXT NOT NULL DEFAULT '', created_at INTEGER NOT NULL);
                INSERT INTO flight_plans (cid, callsign, rules, aircraft, cruise_speed, departure, destination, departure_time,
                    cruise_altitude, enroute_minutes, fuel_minutes, route, created_at)
                VALUES (7, 'OLD1', 'IFR', 'B738', 440, 'UUEE', 'ULLI', '1200', 'FL330', 70, 150, 'DCT', 1);
                """);
        }
        SqliteConnection.ClearAllPools();
        var plans = site.Get<FlightPlanService>();
        Assert.Equal("", plans.Latest(7)!.Waypoints);
    }
}
