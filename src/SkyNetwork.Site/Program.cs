using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site;
using SkyNetwork.Site.Api;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;
using SkyNetwork.Site.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<MemberService>();
builder.Services.AddSingleton<ContentService>();
builder.Services.AddSingleton<FlightPlanService>();
builder.Services.AddSingleton<SupportService>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddHttpClient("feed", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<NetworkFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NetworkFeed>());

// Cyrillic stays as text in the HTML instead of &#x...; entities.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.Cookie.Name = "skynetwork";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Account");
    o.Conventions.AuthorizePage("/FlightPlan");
    o.Conventions.AuthorizePage("/Training");
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Login and registration: a few attempts per minute per address.
    int limit = builder.Configuration.GetValue("Site:AuthAttemptsPerMinute", 10);
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddCors(o => o.AddPolicy("api", p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET")));

var app = builder.Build();
app.Services.GetRequiredService<Database>().Migrate();

if (app.Configuration.GetValue<bool>("Site:BehindProxy"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error/500");
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    var user = ctx.RequestServices.GetRequiredService<CurrentUser>();
    await user.LoadAsync(ctx);
    // The staff area does not exist for anyone else: plain 404, no login redirect, not indexed.
    if (ctx.Request.Path.StartsWithSegments("/staff"))
    {
        if (!user.IsStaff)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        ctx.Response.Headers.CacheControl = "no-store";
    }
    await next();
});
app.UseAuthorization();

app.MapRazorPages();
app.MapSiteApi();

app.Run();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
