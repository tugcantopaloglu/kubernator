namespace Kubernator.Web.Auth;

public sealed record ApiKeyRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ApiKeyScope Scope { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public required bool Disabled { get; init; }
    public required int RateLimitPerMinute { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } e && e <= now;
    public bool IsActive(DateTimeOffset now) => !Disabled && !IsExpired(now);
}

public sealed record ApiKeyCreationResult
{
    public required ApiKeyRecord Record { get; init; }
    public required string PlaintextKey { get; init; }
}
