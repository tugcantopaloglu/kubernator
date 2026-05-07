using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Kubernator.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kubernator.Web.Api;

internal sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string? BootstrapKey { get; set; }
}

internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyStore store;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IApiKeyStore store) : base(options, loggerFactory, encoder)
    {
        this.store = store;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var presentedRaw) || presentedRaw.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }
        var presented = presentedRaw.ToString();
        if (string.IsNullOrEmpty(presented))
        {
            return AuthenticateResult.NoResult();
        }

        if (!string.IsNullOrEmpty(Options.BootstrapKey))
        {
            var presentedBytes = System.Text.Encoding.UTF8.GetBytes(presented);
            var bootstrapBytes = System.Text.Encoding.UTF8.GetBytes(Options.BootstrapKey);
            if (CryptographicOperations.FixedTimeEquals(presentedBytes, bootstrapBytes))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, "bootstrap"),
                    new Claim(ApiKeyScopes.ScopeClaimType, ApiKeyScope.Admin.ToString()),
                    new Claim(ApiKeyScopes.KeyIdClaimType, "bootstrap"),
                    new Claim(ApiKeyScopes.KeyNameClaimType, "bootstrap"),
                    new Claim("auth_method", "api_key_bootstrap")
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
        }

        var record = await store.ResolveByPlaintextAsync(presented, Context.RequestAborted);
        if (record is null)
        {
            Logger.LogWarning("api key rejected for {Path}", Request.Path);
            return AuthenticateResult.Fail("invalid api key");
        }

        var now = DateTimeOffset.UtcNow;
        if (!record.IsActive(now))
        {
            Logger.LogWarning("api key {Id} ({Name}) rejected: disabled={Disabled} expired={Expired}",
                record.Id, record.Name, record.Disabled, record.IsExpired(now));
            return AuthenticateResult.Fail(record.IsExpired(now) ? "api key expired" : "api key disabled");
        }

        try
        {
            await store.TouchUsageAsync(record.Id, now, Context.RequestAborted);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "could not update last_used_at for key {Id}", record.Id);
        }

        var dbClaims = new[]
        {
            new Claim(ClaimTypes.Name, record.Name),
            new Claim(ApiKeyScopes.ScopeClaimType, record.Scope.ToString()),
            new Claim(ApiKeyScopes.KeyIdClaimType, record.Id),
            new Claim(ApiKeyScopes.KeyNameClaimType, record.Name),
            new Claim("auth_method", "api_key")
        };
        var dbIdentity = new ClaimsIdentity(dbClaims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(dbIdentity), Scheme.Name));
    }
}
