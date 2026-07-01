using System.Collections.Concurrent;

namespace Kubernator.Web.Auth;

public sealed class ApiKeyRateLimitCache : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<ApiKeyRateLimitCache> logger;
    private readonly ConcurrentDictionary<string, int> limits = new(StringComparer.Ordinal);

    public ApiKeyRateLimitCache(IServiceScopeFactory scopeFactory, ILogger<ApiKeyRateLimitCache> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    public int GetRateLimit(string keyId, int fallback)
        => limits.TryGetValue(keyId, out var limit) ? limit : fallback;

    public void Set(string keyId, int rateLimitPerMinute) => limits[keyId] = rateLimitPerMinute;

    public void Remove(string keyId) => limits.TryRemove(keyId, out _);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();
            var keys = await store.ListAsync(ct);
            foreach (var key in keys)
            {
                limits[key.Id] = key.RateLimitPerMinute;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "failed to refresh api key rate limits");
        }
    }
}
