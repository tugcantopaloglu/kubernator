namespace Kubernator.Web.Auth;

public sealed record CreateApiKeyOptions
{
    public required string Name { get; init; }
    public required ApiKeyScope Scope { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? RateLimitPerMinute { get; init; }
}

public interface IApiKeyStore
{
    Task<IReadOnlyList<ApiKeyRecord>> ListAsync(CancellationToken ct = default);
    Task<ApiKeyRecord?> GetAsync(string id, CancellationToken ct = default);
    Task<ApiKeyCreationResult> CreateAsync(CreateApiKeyOptions options, CancellationToken ct = default);
    Task<ApiKeyRecord?> ResolveByPlaintextAsync(string plaintext, CancellationToken ct = default);
    Task<bool> DisableAsync(string id, bool disabled, CancellationToken ct = default);
    Task<bool> RemoveAsync(string id, CancellationToken ct = default);
    Task TouchUsageAsync(string id, DateTimeOffset usedAt, CancellationToken ct = default);
}
