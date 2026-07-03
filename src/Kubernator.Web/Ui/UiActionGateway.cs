using System.Diagnostics;
using Kubernator.Web.Auth;
using Kubernator.Web.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Kubernator.Web.Ui;

/// <summary>
/// Thrown when an interactive UI action is refused because the session exceeded its
/// per-minute action budget. Pages catch this and surface it as an error banner.
/// </summary>
public sealed class UiRateLimitException : Exception
{
    public UiRateLimitException(int perMinute)
        : base($"rate limit exceeded — this session may run {perMinute} action(s) per minute; wait a moment and retry")
    {
    }
}

/// <summary>
/// The single choke point Blazor pages use to invoke cluster-affecting Core operations.
/// The REST controllers get audit logging, rate limiting, and key-scoped auth for free from
/// the HTTP middleware pipeline; interactive pages that inject <c>Kubernator.Core</c> services
/// directly bypass all three. Wrapping those calls here closes that gap: every invocation is
/// attributed to the logged-in session user, counted against a per-user rate limit, and
/// written to the same <see cref="AuditLog"/> the API uses — so a UI-driven action is as
/// traceable as a <c>POST /api/v1/...</c> one.
/// </summary>
public sealed class UiActionGateway
{
    private readonly AuditLog audit;
    private readonly UiActionRateLimiter rateLimiter;
    private readonly AuthenticationStateProvider authState;

    public UiActionGateway(AuditLog audit, UiActionRateLimiter rateLimiter, AuthenticationStateProvider authState)
    {
        this.audit = audit;
        this.rateLimiter = rateLimiter;
        this.authState = authState;
    }

    /// <summary>Run an async action that produces a value, with audit + rate limiting.</summary>
    public async Task<T> InvokeAsync<T>(string action, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var (user, method) = await ResolveUserAsync();
        Guard(action, user, method);

        var sw = Stopwatch.StartNew();
        var status = StatusCodes.Status200OK;
        string? error = null;
        try
        {
            return await work(ct);
        }
        catch (Exception ex)
        {
            status = StatusCodes.Status500InternalServerError;
            error = ex.GetType().Name + ": " + ex.Message;
            throw;
        }
        finally
        {
            Record(action, user, method, sw.ElapsedMilliseconds, status, error);
        }
    }

    /// <summary>Run an async action with no return value.</summary>
    public Task InvokeAsync(string action, Func<CancellationToken, Task> work, CancellationToken ct = default)
        => InvokeAsync<object?>(action, async c => { await work(c); return null; }, ct);

    /// <summary>Run a synchronous action (e.g. enqueuing a job) that produces a value.</summary>
    public Task<T> InvokeAsync<T>(string action, Func<T> work)
        => InvokeAsync(action, _ => Task.FromResult(work()));

    private void Guard(string action, string user, string? method)
    {
        if (!rateLimiter.TryAcquire(user))
        {
            Record(action, user, method, 0, StatusCodes.Status429TooManyRequests, "rate limit exceeded");
            throw new UiRateLimitException(rateLimiter.PerMinute);
        }
    }

    private async Task<(string User, string? Method)> ResolveUserAsync()
    {
        try
        {
            var state = await authState.GetAuthenticationStateAsync();
            var name = state.User.Identity?.Name;
            var method = state.User.FindFirst("auth_method")?.Value;
            return (string.IsNullOrEmpty(name) ? "anonymous" : name, method);
        }
        catch
        {
            return ("anonymous", null);
        }
    }

    private void Record(string action, string user, string? method, long durationMs, int status, string? error)
    {
        try
        {
            audit.Write(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                TraceId = Activity.Current?.Id ?? "ui",
                Method = "UI",
                Path = action,
                StatusCode = status,
                DurationMs = durationMs,
                KeyName = user,
                Scope = "session",
                AuthMethod = method ?? "session-cookie",
                Error = error
            });
        }
        catch
        {
            // Auditing must never take down a UI action; the API middleware swallows the
            // same way (see AuditMiddleware).
        }
    }
}
