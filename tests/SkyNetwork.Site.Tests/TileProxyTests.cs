using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;

namespace SkyNetwork.Site.Tests;

public class TileProxyTests
{
    [Fact]
    public async Task TilesComeFromTheFirstWorkingSourceAndAreCached()
    {
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var hits = new List<string>();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                lock (hits) hits.Add(ctx.Request.Url!.AbsolutePath);
                if (ctx.Request.Url!.AbsolutePath.StartsWith("/down/")) ctx.Response.StatusCode = 503;
                else if (ctx.Request.Url!.AbsolutePath.StartsWith("/labels/")) await ctx.Response.OutputStream.WriteAsync("LBL"u8.ToArray());
                else await ctx.Response.OutputStream.WriteAsync("PNG"u8.ToArray());
                ctx.Response.Close();
            }
        });

        string cache = Path.Combine(Path.GetTempPath(), $"skynet-tiles-{Guid.NewGuid():N}");
        using var site = new SiteFactory();
        using var app = site.WithWebHostBuilder(b =>
        {
            b.UseSetting("Site:TileCache", cache);
            b.UseSetting("Site:TileSources:0", $"http://127.0.0.1:{port}/down/{{z}}/{{x}}/{{y}}.png");
            b.UseSetting("Site:TileSources:1", $"http://127.0.0.1:{port}/up/{{z}}/{{x}}/{{y}}.png");
            b.UseSetting("Site:TileLabelSources:0", $"http://127.0.0.1:{port}/labels/{{z}}/{{x}}/{{y}}.png");
        });
        var c = app.CreateClient();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var r = await c.GetAsync("/tiles/3/5/2.png");
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Equal("image/png", r.Content.Headers.ContentType!.MediaType);
                Assert.Equal("PNG", await r.Content.ReadAsStringAsync());
            }
            // First source failed, second answered, the repeat came from the cache.
            lock (hits) Assert.Equal(["/down/3/5/2.png", "/up/3/5/2.png"], hits);
            Assert.True(File.Exists(Path.Combine(cache, "3", "5", "2.png")));


            // Labels are a separate layer with their own sources and cache.
            var labels = await c.GetAsync("/tiles/labels/3/5/2.png");
            Assert.Equal(HttpStatusCode.OK, labels.StatusCode);
            Assert.Equal("LBL", await labels.Content.ReadAsStringAsync());
            Assert.True(File.Exists(Path.Combine(cache, "labels", "3", "5", "2.png")));

            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/tiles/3/8/0.png")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/tiles/19/0/0.png")).StatusCode);
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(cache, true); } catch (IOException) { }
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
