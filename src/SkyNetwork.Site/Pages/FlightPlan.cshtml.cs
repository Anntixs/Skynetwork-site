using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

/// <summary>Flight plan filing; SkyPilot opens /flightplan?callsign=… and reads /api/flightplans/latest.</summary>
public sealed partial class FlightPlanModel(CurrentUser me, FlightPlanService plans) : PageModel
{
    [BindProperty] public FlightPlan Plan { get; set; } = new();
    public bool Saved { get; private set; }
    public string? Error { get; private set; }

    public void OnGet(string? callsign, int? saved)
    {
        Saved = saved == 1;
        // Start from the last plan: most flights are re-filed with small changes.
        Plan = plans.Latest(me.Cid) ?? new FlightPlan { Remarks = "/V/" };
        if (!string.IsNullOrWhiteSpace(callsign)) Plan.Callsign = callsign.Trim().ToUpperInvariant();
    }

    public IActionResult OnPost()
    {
        var p = Plan;
        // Empty inputs arrive as null.
        static string U(string? v) => (v ?? "").Trim().ToUpperInvariant();
        p.Cid = me.Cid;
        p.Callsign = U(p.Callsign);
        p.Rules = p.Rules == "VFR" ? "VFR" : "IFR";
        p.Aircraft = U(p.Aircraft);
        p.Departure = U(p.Departure);
        p.Destination = U(p.Destination);
        p.Alternate = U(p.Alternate);
        p.DepartureTime = U(p.DepartureTime);
        p.CruiseAltitude = U(p.CruiseAltitude);
        p.Route = Regex.Replace(U(p.Route), @"\s+", " ");
        p.Remarks = (p.Remarks ?? "").Trim();
        Error = Validate(p);
        if (Error != null) return Page();
        plans.File(p);
        return Redirect("/flightplan?saved=1");
    }

    [GeneratedRegex("^[A-Z0-9]{2,10}$")] private static partial Regex Callsign();
    [GeneratedRegex("^[A-Z0-9]{4}$")] private static partial Regex Icao();
    [GeneratedRegex("^([01][0-9]|2[0-3])[0-5][0-9]$")] private static partial Regex Hhmm();
    [GeneratedRegex("^(FL[0-9]{2,3}|[0-9]{3,5}|[AF][0-9]{3})$")] private static partial Regex Level();

    private static string? Validate(FlightPlan p)
    {
        if (!Callsign().IsMatch(p.Callsign)) return "Callsign: 2–10 Latin letters and digits";
        if (p.Aircraft.Length < 2 || p.Aircraft.Length > 8) return "Enter the ICAO aircraft type code";
        if (p.CruiseSpeed is < 30 or > 3000) return "Speed: 30 to 3000 knots";
        if (!Icao().IsMatch(p.Departure) || !Icao().IsMatch(p.Destination)) return "Airports: 4-letter ICAO codes";
        if (p.Alternate.Length > 0 && !Icao().IsMatch(p.Alternate)) return "Alternate: a 4-letter ICAO code";
        if (!Hhmm().IsMatch(p.DepartureTime)) return "Departure time: HHMM in UTC, for example 1200";
        if (!Level().IsMatch(p.CruiseAltitude)) return "Cruise level: for example FL350 or 9000";
        if (p.EnrouteMinutes is < 1 or > 2400 || p.FuelMinutes is < 1 or > 3000) return "Check the en route time and fuel";
        if (p.FuelMinutes < p.EnrouteMinutes) return "Less fuel than en route time";
        if (p.Route.Length == 0 || p.Route.Contains(':') || p.Remarks.Contains(':')) return "Route and remarks cannot contain colons";
        return null;
    }
}
