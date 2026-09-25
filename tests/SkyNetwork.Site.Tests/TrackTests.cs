using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Tests;

/// <summary>The flown track on the map: dense while taxiing, sparse in cruise.</summary>
public class TrackTests
{
    private static string Feed(double lat, double lon, int altitude, int groundspeed, bool onGround, long time) => $$"""
        {"general":{"server":"SkyNetwork","update_timestamp":{{time}}},
         "pilots":[{"cid":1000012,"name":"Dmitry Volkov","callsign":"AFL1234","logon_time":1789999000,
           "latitude":{{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"longitude":{{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
           "altitude":{{altitude}},"groundspeed":{{groundspeed}},"heading":130,"on_ground":{{(onGround ? "true" : "false")}},"transponder":"2000","flight_plan":null}],
         "controllers":[]}
        """;

    [Fact]
    public void TaxiFixesAreKept_CruiseFixesAreThinned()
    {
        using var site = new SiteFactory();
        var feed = site.Get<NetworkFeed>();
        // Taxiing: ~30 m between fixes (0.00027° of latitude), every one is a corner of the taxi route.
        feed.Ingest(Feed(55.4100, 37.9000, 600, 12, true, 1790000000));
        feed.Ingest(Feed(55.4103, 37.9000, 600, 12, true, 1790000005));
        feed.Ingest(Feed(55.4103, 37.9005, 600, 14, true, 1790000010));
        Assert.Equal(3, feed.Track("AFL1234").Count);

        // Standing still at the holding point: no new points.
        feed.Ingest(Feed(55.4103, 37.9005, 600, 0, true, 1790000015));
        Assert.Equal(3, feed.Track("AFL1234").Count);

        // Airborne: a 30 m move is not worth a point, 0.3 nm is.
        feed.Ingest(Feed(55.4106, 37.9005, 700, 250, false, 1790000020));
        Assert.Equal(3, feed.Track("AFL1234").Count);
        feed.Ingest(Feed(55.4160, 37.9005, 1100, 260, false, 1790000025));
        Assert.Equal(4, feed.Track("AFL1234").Count);
        Assert.Equal((1100, 260), (feed.Track("AFL1234")[^1].Altitude, feed.Track("AFL1234")[^1].Groundspeed));
    }
}
