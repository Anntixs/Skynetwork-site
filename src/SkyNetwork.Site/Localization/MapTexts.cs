using System.Text.Json;

namespace SkyNetwork.Site.Localization;

/// <summary>Texts the map script shows (wwwroot/js/map.js), handed to it translated in the map's data-text attribute.</summary>
public static class MapTexts
{
    public static readonly string[] Keys =
    [
        "Updated", "Server not responding", "Offline", "Close", "{0} min", "{0} h {1} min", "arrival ≈ {0}", "Next point:",
        "{0} route points from SimBrief", "{0} points found in the VOR/NDB database, without the points along airways",
        "Route points appear when the pilot plans in SimBrief and has loaded a plan on the Flight plan page once.",
        "On the ground", "No flight plan filed", "Altitude", "Ground speed", "Heading", "Squawk", "Cruise level", "TAS", "Departure",
        "En route", "Fuel", "Alternate:", "Route", "Remarks", "Online for {0}", "Online for", "Frequency", "Rating", "Sector:", "Airport:",
        "none", "no data", "Loading…", "Controllers", "nobody", "Departures", "Arrivals", "Not found: {0}",
    ];

    public static string Json(Lang l) => JsonSerializer.Serialize(Keys.ToDictionary(k => k, k => l[k]));
}
