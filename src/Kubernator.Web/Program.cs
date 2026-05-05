using System.Security.Claims;
using System.Security.Cryptography;
using Kubernator.Core.DependencyInjection;
using Kubernator.Runtime.DependencyInjection;
using Kubernator.Web.Auth;
using Kubernator.Web.Components;
using Kubernator.Web.Downloads;
using Kubernator.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddKubernatorCore();
builder.Services.AddKubernatorRuntime();
builder.Services.AddSingleton<ArtifactRegistry>();
builder.Services.AddScoped<BuildPipeline>();
builder.Services.AddSingleton<ManifestAuditor>();
builder.Services.AddSingleton<AuthService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/auth/login";
        options.LogoutPath = "/api/auth/logout";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Name = "kubernator.auth";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();
builder.Services.AddHttpContextAccessor();

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
var allowInsecureNetwork = string.Equals(builder.Configuration["allow-insecure-network"], "true", StringComparison.OrdinalIgnoreCase);

if (!isLoopbackOnly && !allowInsecureNetwork)
{
    app.Logger.LogCritical(
        "kubernator.web refuses to bind to non-loopback host {Host} over plain HTTP. " +
        "the auth cookie would travel in cleartext and be capturable on the network. " +
        "either bind to 127.0.0.1 and front this with a TLS-terminating reverse proxy, " +
        "or pass --allow-insecure-network=true to override (not recommended).", listenHost);
    return;
}
if (!isLoopbackOnly)
{
    app.Logger.LogWarning(
        "kubernator.web is bound to {Host} over plain HTTP with --allow-insecure-network=true. " +
        "the auth cookie + TOTP secret + bundle uploads travel in cleartext on this network.", listenHost);
}
if (!isLoopbackOnly && !requiresToken)
{
    app.Logger.LogWarning(
        "kubernator.web is bound to {Host} without --token; the cookie session is the only barrier. " +
        "consider passing --token <hex> as an extra layer.", listenHost);
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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/download/{token}", [Authorize] (string token, ArtifactRegistry registry) =>
{
    var entry = registry.Resolve(token);
    if (entry is null || !File.Exists(entry.FilePath))
    {
        return Results.NotFound();
    }
    var stream = File.OpenRead(entry.FilePath);
    return Results.File(stream, "application/octet-stream", entry.DownloadName);
});

app.MapPost("/api/auth/setup", async (HttpRequest req, AuthService auth, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(req.HttpContext);
    if (await auth.IsConfiguredAsync())
    {
        return Results.Redirect("/auth/login?error=" + Uri.EscapeDataString("already configured"));
    }
    var form = await req.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var confirm = form["confirm"].ToString();
    if (!string.Equals(password, confirm, StringComparison.Ordinal))
    {
        return Results.Redirect("/auth/setup?error=" + Uri.EscapeDataString("passwords do not match"));
    }
    try
    {
        var result = await auth.SetupAsync(username, password);
        var ticket = auth.IssueSetupTicket(result);
        return Results.Redirect("/auth/setup-complete?ticket=" + Uri.EscapeDataString(ticket));
    }
    catch (Exception ex)
    {
        return Results.Redirect("/auth/setup?error=" + Uri.EscapeDataString(ex.Message));
    }
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (HttpRequest req, AuthService auth, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(req.HttpContext);
    var form = await req.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var code = form["code"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    if (!IsLocalRelativeUrl(returnUrl))
    {
        returnUrl = "/";
    }

    var ok = await auth.SignInAsync(username, password, code);
    if (!ok)
    {
        var qs = "?error=" + Uri.EscapeDataString("invalid credentials or one-time code");
        if (!string.IsNullOrEmpty(returnUrl) && returnUrl != "/")
        {
            qs += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        }
        return Results.Redirect("/auth/login" + qs);
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username.Trim()),
        new Claim("auth_method", "password+totp")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await req.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect(returnUrl);
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(ctx);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/auth/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsLocalRelativeUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;
    if (url.Length == 1 && url[0] == '/') return true;
    if (url[0] != '/') return false;
    if (url[1] == '/' || url[1] == '\\') return false;
    if (url.Contains(':', StringComparison.Ordinal)) return false;
    return true;
}
