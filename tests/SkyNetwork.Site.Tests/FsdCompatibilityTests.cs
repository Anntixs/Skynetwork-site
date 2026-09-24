using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

/// <summary>
/// The site and skynet-fsd share one database. Runs only when SKYNET_FSD_BUILD points at the
/// Skynetwork-fsd build directory (skynet-fsd, skynet-admin).
/// </summary>
public class FsdCompatibilityTests
{
    private static readonly string? Build = Environment.GetEnvironmentVariable("SKYNET_FSD_BUILD");

    [Fact]
    public async Task AccountsWorkBothWays()
    {
        if (string.IsNullOrEmpty(Build)) return;
        using var site = new SiteFactory();
        var members = site.Get<MemberService>(); // creates the schema

        // Created with the server's admin tool → signs in on the site.
        Run("skynet-admin", $"--db {site.DatabasePath} adduser 1500000 \"Admin Tool\" toolpass1 S2");
        var fromTool = members.Authenticate(1500000, "toolpass1");
        Assert.NotNull(fromTool);
        Assert.Equal(Ratings.S2, fromTool!.Rating);

        // Registered on the site → logs in to the FSD server; suspended on the site → refused.
        long cid = members.Register("Site Pilot", "pilot@example.com", "", "sitepass1");
        int port = FreePort();
        using var fsd = Process.Start(new ProcessStartInfo(Path.Combine(Build, "skynet-fsd"),
            $"--db {site.DatabasePath} --host 127.0.0.1 --port {port} --http-port {FreePort()}") { RedirectStandardError = true })!;
        try
        {
            await Task.Delay(300);
            Assert.StartsWith("#TMSERVER", await LoginAsync(port, $"#APSBI1:SERVER:{cid}:sitepass1:1:100:1:Site Pilot"));
            Assert.StartsWith("$ER", await LoginAsync(port, $"#APSBI2:SERVER:{cid}:wrongpass:1:100:1:Site Pilot"));
            members.SetSuspended(0, cid, true, "test");
            Assert.StartsWith("$ER", await LoginAsync(port, $"#APSBI3:SERVER:{cid}:sitepass1:1:100:1:Site Pilot"));
        }
        finally
        {
            fsd.Kill();
        }
    }

    private static async Task<string> LoginAsync(int port, string login)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(login + "\r\n"));
        using var reader = new StreamReader(stream);
        var read = reader.ReadLineAsync();
        return await read.WaitAsync(TimeSpan.FromSeconds(5)) ?? "";
    }

    private static void Run(string tool, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Build!, tool), args) { RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
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
