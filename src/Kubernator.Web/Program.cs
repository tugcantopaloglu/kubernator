using System.Security.Claims;
using System.Security.Cryptography;
using Kubernator.Core.DependencyInjection;
using Kubernator.Core.Updates;
using Kubernator.Runtime.DependencyInjection;
using System.Threading.RateLimiting;
using Kubernator.Web.Api;
using Kubernator.Web.Auth;
using Kubernator.Web.Components;
using Kubernator.Web.Downloads;
using Kubernator.Web.Jobs;
using Kubernator.Web.Jobs.Handlers;
using Kubernator.Web.Logging;
using Kubernator.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    .ApplyKubernatorSinks()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .ApplyKubernatorSinks());

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddKubernatorCore();
    builder.Services.AddKubernatorRuntime();
    builder.Services.AddSingleton<ArtifactRegistry>();
    builder.Services.AddScoped<BuildPipeline>();
    builder.Services.AddSingleton<AuthService>();
    builder.Services.AddSingleton<IApiKeyStore, SqliteApiKeyStore>();
    builder.Services.AddSingleton<ApiKeyRateLimitCache>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ApiKeyRateLimitCache>());
    builder.Services.AddSingleton<AuditLog>();
    builder.Services.AddSingleton<Kubernator.Web.Ui.UiActionRateLimiter>();
    builder.Services.AddScoped<Kubernator.Web.Ui.UiActionGateway>();
    builder.Services.AddSingleton<SqliteJobManager>();
    builder.Services.AddSingleton<IJobManager>(sp => sp.GetRequiredService<SqliteJobManager>());
    builder.Services.AddSingleton<IJobHandler, ValidateJobHandler>();
    builder.Services.AddSingleton<IJobHandler, BuildJobHandler>();
    builder.Services.AddSingleton<IJobHandler, BundleCreateJobHandler>();
    builder.Services.AddSingleton<IJobHandler, ClusterPullJobHandler>();
    builder.Services.AddSingleton<IJobHandler, ClusterInstallJobHandler>();
    builder.Services.AddSingleton<IJobHandler, ClusterUpgradeJobHandler>();
    builder.Services.AddSingleton<IJobHandler, ClusterStatusJobHandler>();
    builder.Services.AddHostedService<JobBackgroundRunner>();
    builder.Services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();

    var trustProxyHeaders = string.Equals(builder.Configuration["trust-proxy-headers"], "true", StringComparison.OrdinalIgnoreCase);
    var bootstrapKey = builder.Configuration["api-key"] ?? Environment.GetEnvironmentVariable("KUBERNATOR_API_KEY");
    var apiKeyOptions = new ApiKeyOptions { BootstrapKey = bootstrapKey };
    builder.Services.AddSingleton(apiKeyOptions);

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/auth/login";
            options.AccessDeniedPath = "/auth/login";
            options.LogoutPath = "/api/auth/logout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = trustProxyHeaders
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            options.Cookie.Name = "kubernator.auth";
            options.ExpireTimeSpan = KubernatorAuthDefaults.SessionLifetime;
            options.SlidingExpiration = true;
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyOptions.SchemeName,
            options => { options.BootstrapKey = bootstrapKey; });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.AddPolicy(ApiKeyScopes.ReadPolicy, policy => policy
            .AddAuthenticationSchemes(ApiKeyOptions.SchemeName)
            .RequireAuthenticatedUser()
            .Requirements.Add(new ScopeRequirement(ApiKeyScope.Read)));
        options.AddPolicy(ApiKeyScopes.GeneratePolicy, policy => policy
            .AddAuthenticationSchemes(ApiKeyOptions.SchemeName)
            .RequireAuthenticatedUser()
            .Requirements.Add(new ScopeRequirement(ApiKeyScope.Generate)));
        options.AddPolicy(ApiKeyScopes.AdminPolicy, policy => policy
            .AddAuthenticationSchemes(ApiKeyOptions.SchemeName)
            .RequireAuthenticatedUser()
            .Requirements.Add(new ScopeRequirement(ApiKeyScope.Admin)));
        options.AddPolicy(ApiKeyScopes.DownloadPolicy, policy => policy
            .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyOptions.SchemeName)
            .RequireAuthenticatedUser());
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api/v1"))
            {
                return RateLimitPartition.GetNoLimiter("__none");
            }
            var keyId = ctx.User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value;
            var partitionKey = keyId ?? ("ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
            var perMinute = SqliteApiKeyStore.DefaultRateLimitPerMinute;
            if (keyId is { Length: > 0 } && keyId != "bootstrap")
            {
                var cache = ctx.RequestServices.GetService<ApiKeyRateLimitCache>();
                if (cache is not null)
                {
                    perMinute = cache.GetRateLimit(keyId, perMinute);
                }
            }
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        });
        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.ContentType = "application/problem+json";
            var problem = new ApiProblem
            {
                Type = "https://kubernator/errors/rate-limit",
                Title = "rate limit exceeded",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = "too many requests; reduce traffic or request a higher per-key limit",
                Instance = ctx.HttpContext.Request.Path,
                TraceId = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier
            };
            await System.Text.Json.JsonSerializer.SerializeAsync(ctx.HttpContext.Response.Body, problem, cancellationToken: ct);
        };
    });

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddAntiforgery();
    builder.Services.AddHttpContextAccessor();

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState
                    .Where(kv => kv.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        kv => string.IsNullOrEmpty(kv.Key) ? "body" : kv.Key,
                        kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message ?? "invalid" : e.ErrorMessage).ToArray());
                var problem = new ApiProblem
                {
                    Type = "https://kubernator/errors/validation",
                    Title = "validation failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "one or more fields failed validation",
                    Instance = ctx.HttpContext.Request.Path,
                    TraceId = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier,
                    Errors = errors
                };
                return new Microsoft.AspNetCore.Mvc.ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Kubernator API",
            Version = "v1",
            Description = "REST API for kubernator. Authenticate every /api/v1 request with the X-Api-Key header."
        });
        options.AddSecurityDefinition(ApiKeyOptions.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyOptions.HeaderName,
            In = ParameterLocation.Header,
            Description = "API key configured via --api-key or KUBERNATOR_API_KEY."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = ApiKeyOptions.SchemeName
                }
            }] = Array.Empty<string>()
        });
    });

    if (trustProxyHeaders)
    {
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

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

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex is not null) return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 500) return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400) return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "");
            diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "");
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    var listenHost = ParseBind(bindAddress).Host;
    var requiresToken = !string.IsNullOrEmpty(accessToken);
    var isLoopbackOnly = listenHost is "127.0.0.1" or "::1" or "localhost";
    var allowInsecureNetwork = string.Equals(builder.Configuration["allow-insecure-network"], "true", StringComparison.OrdinalIgnoreCase);

    if (!isLoopbackOnly && !allowInsecureNetwork && !trustProxyHeaders)
    {
        app.Logger.LogCritical(
            "kubernator.web refuses to bind to non-loopback host {Host} over plain HTTP. " +
            "the auth cookie would travel in cleartext and be capturable on the network. " +
            "either bind to 127.0.0.1 and front this with a TLS-terminating reverse proxy, " +
            "pass --trust-proxy-headers=true if a TLS-terminating proxy in front sets X-Forwarded-Proto, " +
            "or pass --allow-insecure-network=true to override (not recommended).", listenHost);
        return;
    }
    if (!isLoopbackOnly && !trustProxyHeaders)
    {
        app.Logger.LogWarning(
            "kubernator.web is bound to {Host} over plain HTTP with --allow-insecure-network=true. " +
            "the auth cookie + TOTP secret + bundle uploads travel in cleartext on this network.", listenHost);
    }
    if (trustProxyHeaders)
    {
        app.Logger.LogInformation(
            "kubernator.web trusts X-Forwarded-* headers from the upstream proxy and enforces Secure cookies. " +
            "ensure only your reverse proxy can reach this host on the network.");
    }
    if (!isLoopbackOnly && !requiresToken)
    {
        app.Logger.LogWarning(
            "kubernator.web is bound to {Host} without --token; the cookie session is the only barrier. " +
            "consider passing --token <hex> as an extra layer.", listenHost);
    }
    if (apiKeyOptions.IsConfigured)
    {
        app.Logger.LogInformation("kubernator.web api enabled at /api/v1; clients must present {Header}.", ApiKeyOptions.HeaderName);
    }
    else
    {
        app.Logger.LogInformation("kubernator.web api disabled — pass --api-key or set KUBERNATOR_API_KEY to enable /api/v1.");
    }

    if (trustProxyHeaders)
    {
        app.UseForwardedHeaders();
    }

    app.Use(async (ctx, next) =>
    {
        var headers = ctx.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        var isSwaggerUi = ctx.Request.Path.StartsWithSegments("/swagger");
        headers["Content-Security-Policy"] = isSwaggerUi
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'"
            : "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'none'";
        await next();
    });

    if (requiresToken)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api/v1") && apiKeyOptions.IsConfigured)
            {
                await next();
                return;
            }
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

    if (apiKeyOptions.IsConfigured)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Kubernator API v1");
            options.DocumentTitle = "Kubernator API";
        });
    }

    app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/v1"),
        branch =>
        {
            branch.UseApiExceptionHandler();
            branch.UseAuditLog();
        });

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.UseRateLimiter();

    app.MapControllers();

    app.MapGet("/download/{token}", [Authorize(Policy = ApiKeyScopes.DownloadPolicy)] (string token, ArtifactRegistry registry) =>
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
        var recovery = form["recovery"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        if (!IsLocalRelativeUrl(returnUrl))
        {
            returnUrl = "/";
        }

        SignInResult result = !string.IsNullOrEmpty(recovery)
            ? await auth.SignInWithRecoveryAsync(username, password, recovery)
            : await auth.SignInAsync(username, password, code);

        if (result.Outcome != SignInOutcome.Ok)
        {
            var msg = result.Outcome switch
            {
                SignInOutcome.LockedOut => $"too many failed attempts — try again after {result.LockoutUntil:HH:mm} UTC",
                SignInOutcome.Replay => result.Message ?? "this code was already used",
                _ => result.RemainingAttempts > 0
                    ? $"invalid credentials or one-time code ({result.RemainingAttempts} attempt(s) left before lockout)"
                    : "invalid credentials or one-time code"
            };
            var qs = "?error=" + Uri.EscapeDataString(msg);
            if (!string.IsNullOrEmpty(returnUrl) && returnUrl != "/")
            {
                qs += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
            }
            return Results.Redirect("/auth/login" + qs);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username.Trim()),
            new Claim("auth_method", string.IsNullOrEmpty(recovery) ? "password+totp" : "password+recovery")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = false,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(KubernatorAuthDefaults.SessionLifetime)
        };
        await req.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);
        return Results.Redirect(returnUrl);
    }).AllowAnonymous();

    app.MapPost("/api/auth/setup/test-totp", async (HttpRequest req, AuthService auth, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(req.HttpContext);
        var form = await req.ReadFormAsync();
        var ticket = form["ticket"].ToString();
        var code = form["code"].ToString();
        if (auth.PeekSetupTicket(ticket) is null)
        {
            return Results.Redirect("/auth/setup-complete?error=" + Uri.EscapeDataString("setup ticket expired"));
        }
        var ok = auth.TestTotpCode(code);
        var qs = ok ? "&verified=1" : "&error=" + Uri.EscapeDataString("authenticator code did not match — re-scan the secret");
        return Results.Redirect("/auth/setup-complete?ticket=" + Uri.EscapeDataString(ticket) + qs);
    }).AllowAnonymous();

    app.MapPost("/api/auth/logout", async (HttpContext ctx, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(ctx);
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/auth/login");
    });

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Logger.LogInformation("kubernator.web {Version} starting on {Host}", KubernatorVersion.Current, bindAddress);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "kubernator.web terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static bool IsLocalRelativeUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;
    if (url.Length == 1 && url[0] == '/') return true;
    if (url[0] != '/') return false;
    if (url[1] == '/' || url[1] == '\\') return false;
    if (url.Contains(':', StringComparison.Ordinal)) return false;
    return true;
}

internal static class KubernatorAuthDefaults
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
}

public partial class Program;
