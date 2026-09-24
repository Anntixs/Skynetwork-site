using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

public class StaffRankTests
{
    [Fact]
    public async Task RankIsApartFromTheControllerRating_ButShownAsOne()
    {
        using var site = new SiteFactory();
        long admin = site.Member("Anna Admin", Ratings.ADM);
        long sup = site.Member("Sergey Supervisor");
        site.Get<MemberService>().SetRating(0, sup, Ratings.C1);

        var a = site.Browser();
        await a.LoginAsync(admin);
        await a.SubmitPageFormAsync($"/staff/members/{sup}", "StaffRank", new Dictionary<string, string> { ["rank"] = Ratings.SUP.ToString() });
        var m = site.Get<MemberService>().Find(sup)!;
        Assert.Equal((Ratings.C1, Ratings.SUP), (m.Rating, m.StaffRank));

        // Profile: one rating card, the rank with the controller rating next to it.
        var profile = await site.Browser().HtmlAsync($"/members/{sup}");
        Assert.Contains("Supervisor", profile);
        Assert.Contains("Enroute Controller", profile);
        var json = JsonDocument.Parse(await site.Browser().GetStringAsync($"/api/v1/members/{sup}")).RootElement;
        Assert.Equal(("C1", "SUP"), (json.GetProperty("rating").GetString(), json.GetProperty("staffRank").GetString()));

        // The supervisor now has the staff area; changing their controller rating keeps the rank.
        var s = site.Browser();
        await s.LoginAsync(sup);
        await s.HtmlAsync("/staff");
        await a.SubmitPageFormAsync($"/staff/members/{sup}", "Rating", new Dictionary<string, string> { ["rating"] = Ratings.C3.ToString() });
        Assert.Equal((Ratings.C3, Ratings.SUP), (site.Get<MemberService>().Find(sup)!.Rating, site.Get<MemberService>().Find(sup)!.StaffRank));
        Assert.Contains(site.Get<AuditService>().Recent(), e => e.Action == "staff-rank" && e.Details == "— → SUP");

        // Only administrators see the rank form; a supervisor does not.
        long other = site.Member("Olga Member");
        Assert.DoesNotContain("handler=StaffRank", await s.HtmlAsync($"/staff/members/{other}"));
        var r = await s.SubmitAsync($"/staff/members/{other}", new Dictionary<string, string> { ["rank"] = Ratings.ADM.ToString() },
            $"/staff/members/{other}?handler=StaffRank");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, site.Get<MemberService>().Find(other)!.StaffRank);
    }

    [Fact]
    public void OldDatabaseMovesRanksOutOfTheRating()
    {
        string path = Path.Combine(Path.GetTempPath(), $"skynet-old-{Guid.NewGuid():N}.db");
        try
        {
            using (var c = new SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                c.Execute("""
                    CREATE TABLE members (cid INTEGER PRIMARY KEY, name TEXT NOT NULL, rating INTEGER NOT NULL DEFAULT 1,
                        salt BLOB NOT NULL, hash BLOB NOT NULL, suspended INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO members VALUES (1, 'Admin', 12, x'00', x'00', 0), (2, 'Sup', 11, x'00', x'00', 0), (3, 'Ctl', 5, x'00', x'00', 0);
                    """);
            }
            var db = new Database(Microsoft.Extensions.Options.Options.Create(new SiteOptions { Database = path }));
            db.Migrate();
            db.Migrate(); // twice: nothing moves again
            using var check = db.Open();
            var rows = check.Query<(long, long, long)>("SELECT cid, rating, staff_rank FROM members ORDER BY cid").ToList();
            Assert.Equal([(1L, 1L, 12L), (2L, 1L, 11L), (3L, 5L, 0L)], rows);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(f); } catch (IOException) { }
        }
    }
}
