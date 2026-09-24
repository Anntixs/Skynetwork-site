using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>Lifts temporary suspensions when their time is up (checked every minute).</summary>
public sealed class SuspensionExpiry(MemberService members, ILogger<SuspensionExpiry> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                foreach (var cid in members.LiftExpiredSuspensions()) log.LogInformation("Suspension of {Cid} expired", cid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Could not lift expired suspensions");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
