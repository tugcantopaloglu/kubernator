using Kubernator.Web.Auth;

namespace Kubernator.Web.Api;

public sealed record CreateApiKeyRequest
{
    public required string Name { get; init; }
    public required string Scope { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? RateLimitPerMinute { get; init; }
}

public sealed record CreateApiKeyResponse
{
    public required ApiKeyDto Record { get; init; }
    public required string PlaintextKey { get; init; }
}

public sealed record ApiKeyDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Scope { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public required bool Disabled { get; init; }
    public required int RateLimitPerMinute { get; init; }

    public static ApiKeyDto From(ApiKeyRecord r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Scope = r.Scope.ToString(),
        CreatedAt = r.CreatedAt,
        ExpiresAt = r.ExpiresAt,
        LastUsedAt = r.LastUsedAt,
        Disabled = r.Disabled,
        RateLimitPerMinute = r.RateLimitPerMinute
    };
}

public sealed record ApiKeyListResponse
{
    public required IReadOnlyList<ApiKeyDto> Keys { get; init; }
}

public sealed record DisableApiKeyRequest
{
    public required bool Disabled { get; init; }
}
