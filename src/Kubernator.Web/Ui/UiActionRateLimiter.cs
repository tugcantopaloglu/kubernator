using System.Threading.RateLimiting;
using Kubernator.Web.Auth;

namespace Kubernator.Web.Ui;

/// <summary>
/// Per-session fixed-window rate limiter for interactive UI actions. The HTTP
/// <c>GlobalLimiter</c> in <c>Program.cs</c> only sees <c>/api/v1</c> requests, so Blazor
/// circuit actions (which travel over the SignalR connection, not the REST pipeline) are
/// invisible to it. This mirrors that limiter for the UI, partitioned by the logged-in
/// username instead of an API key id, using the same default per-minute budget.
/// </summary>
public sealed class UiActionRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;

    public int PerMinute { get; }

    public UiActionRateLimiter() : this(SqliteApiKeyStore.DefaultRateLimitPerMinute) { }

    public UiActionRateLimiter(int perMinute)
    {
        PerMinute = perMinute;
        limiter = PartitionedRateLimiter.Create<string, string>(user =>
            RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    }

    /// <summary>Consume one permit for <paramref name="user"/>; false when the window is exhausted.</summary>
    public bool TryAcquire(string user)
    {
        using var lease = limiter.AttemptAcquire(user);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
