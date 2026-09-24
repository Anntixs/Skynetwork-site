using System.Net;
using System.Text.Json;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Tests;

public class MapDataTests
{
    [Fact]
    public async Task RegistrationTakesTheCountryFromTheList()
    {
        using var site = new SiteFactory();
        var c = site.Browser();
        Assert.Contains("<option value=\"Россия\">Россия</option>", await c.HtmlAsync("/register"));
        var fields = new Dictionary<string, string>
        {
            ["Name"] = "Anna Smirnova", ["Email"] = "anna@example.com", ["Country"] = "Нарния",
            ["Password"] = "secret123", ["Confirm"] = "secret123", ["AcceptRules"] = "true",
        };
        Assert.Contains("Выберите страну из списка", await (await c.SubmitAsync("/register", fields)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await c.SubmitAsync("/register", new Dictionary<string, string>(fields) { ["Country"] = "Казахстан" })).StatusCode);
        Assert.True(Countries.IsKnown("россия"));
        Assert.Equal("Россия", Countries.All[0]);
    }

    [Fact]
    public void AirportDiagramKeepsRunwaysTaxiwaysAreasAndStands()
    {
        const string overpass = """
            {"elements":[
              {"type":"way","id":1,"tags":{"aeroway":"runway","ref":"06R/24L","width":"60"},
               "geometry":[{"lat":55.9601234,"lon":37.3901234},{"lat":55.98,"lon":37.45}]},
              {"type":"way","id":2,"tags":{"aeroway":"taxiway","ref":"A"},"geometry":[{"lat":55.96,"lon":37.40},{"lat":55.961,"lon":37.41}]},
              {"type":"way","id":3,"tags":{"aeroway":"taxilane"},"geometry":[{"lat":55.962,"lon":37.40},{"lat":55.963,"lon":37.41}]},
              {"type":"way","id":4,"tags":{"aeroway":"parking_position","ref":"101"},"geometry":[{"lat":55.964,"lon":37.40},{"lat":55.965,"lon":37.401}]},
              {"type":"way","id":5,"tags":{"aeroway":"terminal"},"geometry":[{"lat":55.97,"lon":37.4},{"lat":55.97,"lon":37.41},{"lat":55.971,"lon":37.41},{"lat":55.97,"lon":37.4}]},
              {"type":"relation","id":6,"tags":{"aeroway":"apron"},"members":[
                 {"type":"way","role":"outer","geometry":[{"lat":1,"lon":1},{"lat":1,"lon":2}]},
                 {"type":"way","role":"outer","geometry":[{"lat":2,"lon":1},{"lat":1,"lon":1}]},
                 {"type":"way","role":"outer","geometry":[{"lat":1,"lon":2},{"lat":2,"lon":1}]},
                 {"type":"way","role":"inner","geometry":[{"lat":1.5,"lon":1.5},{"lat":1.6,"lon":1.5}]}]},
              {"type":"node","id":7,"lat":55.966,"lon":37.402,"tags":{"aeroway":"gate","ref":"D5"}},
              {"type":"node","id":8,"lat":55.966,"lon":37.402}
            ]}
            """;
        var d = JsonDocument.Parse(AirportLayout.Reduce("UUEE", overpass)).RootElement;
        var rwy = d.GetProperty("runways")[0];
        Assert.Equal(("06R/24L", 60.0), (rwy.GetProperty("ref").GetString(), rwy.GetProperty("width").GetDouble()));
        Assert.Equal(55.96012, rwy.GetProperty("line")[0][0].GetDouble());

        var twys = d.GetProperty("taxiways").EnumerateArray().ToList();
        Assert.Equal(2, twys.Count);
        Assert.Equal((23.0, false), (twys[0].GetProperty("width").GetDouble(), twys[0].GetProperty("lane").GetBoolean()));
        Assert.True(twys[1].GetProperty("lane").GetBoolean());

        // The apron's three outer pieces joined into one closed ring; the inner way left out.
        var areas = d.GetProperty("areas").EnumerateArray().ToList();
        Assert.Equal(["terminal", "apron"], areas.Select(a => a.GetProperty("kind").GetString()));
        var ring = areas[1].GetProperty("ring").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
        Assert.Equal(4, ring.Count);
        Assert.Equal(ring[0], ring[^1]);

        // A stand drawn as a line is where it ends; gates are marked.
        var stands = d.GetProperty("stands").EnumerateArray().ToList();
        Assert.Equal(2, stands.Count);
        Assert.Equal(("101", false, 55.965), (stands[0].GetProperty("ref").GetString(), stands[0].GetProperty("gate").GetBoolean(), stands[0].GetProperty("at")[0].GetDouble()));
        Assert.True(stands[1].GetProperty("gate").GetBoolean());
    }
}
