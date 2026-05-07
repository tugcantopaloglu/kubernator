using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kubernator.Web.Api;

internal sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string? ApiKey { get; set; }
}

internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder) : base(options, loggerFactory, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("api key not configured"));
        }

        if (!Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var presented) || string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presentedBytes = System.Text.Encoding.UTF8.GetBytes(presented.ToString());
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(Options.ApiKey);
        if (!CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes))
        {
            Logger.LogWarning("api key rejected for {Path}", Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("invalid api key"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-client"),
            new Claim("auth_method", "api_key")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
