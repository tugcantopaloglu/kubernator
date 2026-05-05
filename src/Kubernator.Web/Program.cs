using System.Security.Cryptography;
using Kubernator.Core.DependencyInjection;
using Kubernator.Runtime.DependencyInjection;
using Kubernator.Web.Components;
using Kubernator.Web.Downloads;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddKubernatorCore();
builder.Services.AddKubernatorRuntime();
builder.Services.AddSingleton<ArtifactRegistry>();

var bindAddress = builder.Configuration["bind"] ?? "127.0.0.1:5050";
var accessToken = builder.Configuration["token"];

builder.WebHost.ConfigureKestrel(options =>
{
    var (host, port) = ParseBind(bindAddress);
    if (host == "127.0.0.1" || host == "localhost")
    {
        options.ListenLocalhost(port);
    }
    else if (System.Net.IPAddress.TryParse(host, out var ip))
    {
        options.Listen(ip, port);
    }
    else
    {
        options.ListenAnyIP(port);
    }
});

static (string Host, int Port) ParseBind(string raw)
{
    var idx = raw.LastIndexOf(':');
    if (idx > 0 && int.TryParse(raw[(idx + 1)..], out var p))
    {
        return (raw[..idx].Trim('[', ']'), p);
    }
    return (raw, 5050);
}

var app = builder.Build();

var listenHost = ParseBind(bindAddress).Host;
var requiresToken = !string.IsNullOrEmpty(accessToken);
var isLoopbackOnly = listenHost is "127.0.0.1" or "::1" or "localhost";

if (!isLoopbackOnly && !requiresToken)
{
    app.Logger.LogWarning(
        "kubernator.web is bound to {Host} without --token; anyone reaching this port can drive the tool. " +
        "Pass --token <hex> or bind to 127.0.0.1.", listenHost);
}

app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'none'";
    await next();
});

if (requiresToken)
{
    app.Use(async (ctx, next) =>
    {
        var presented = ctx.Request.Query["token"].ToString();
        if (string.IsNullOrEmpty(presented) && ctx.Request.Headers.TryGetValue("X-Kubernator-Token", out var headerToken))
        {
            presented = headerToken.ToString();
        }
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(accessToken!)))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("missing or invalid token");
            return;
        }
        await next();
    });
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/download/{token}", (string token, ArtifactRegistry registry) =>
{
    var entry = registry.Resolve(token);
    if (entry is null || !File.Exists(entry.FilePath))
    {
        return Results.NotFound();
    }
    var stream = File.OpenRead(entry.FilePath);
    return Results.File(stream, "application/octet-stream", entry.DownloadName);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
