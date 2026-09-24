using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

/// <summary>SkyPilot asks only for CID and password; the website confirms them and supplies the name.</summary>
public class PilotLoginTests
{
    [Fact]
    public async Task SkyPilotChecksTheAccount()
    {
        using var site = new SiteFactory();
        long cid = site.Member("Pilot One", Ratings.C1);
        var c = site.Browser();

        var ok = await c.PostAsJsonAsync("/api/v1/auth/pilot", new { cid, password = "password1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((cid, "Pilot One", Ratings.C1, "C1"), (json.GetProperty("cid").GetInt64(), json.GetProperty("name").GetString(),
            json.GetProperty("rating").GetInt32(), json.GetProperty("ratingName").GetString()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/pilot", new { cid, password = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/pilot", new { cid = 999999, password = "password1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/pilot", new { cid, password = "" })).StatusCode);
    }
}
