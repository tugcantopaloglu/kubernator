using Kubernator.Web.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernator.Web.Tests.Auth;

public sealed class ApiKeyRateLimitCacheTests
{
    private static ApiKeyRateLimitCache CreateSut()
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new ApiKeyRateLimitCache(scopeFactory, NullLogger<ApiKeyRateLimitCache>.Instance);
    }

    [Fact]
    public void GetRateLimit_returns_fallback_for_unknown_key()
    {
        var sut = CreateSut();

        sut.GetRateLimit("unknown", 42).Should().Be(42);
    }

    [Fact]
    public void Set_then_GetRateLimit_returns_the_cached_value()
    {
        var sut = CreateSut();

        sut.Set("key-1", 7);

        sut.GetRateLimit("key-1", 42).Should().Be(7);
    }

    [Fact]
    public void Remove_falls_back_again()
    {
        var sut = CreateSut();
        sut.Set("key-1", 7);

        sut.Remove("key-1");

        sut.GetRateLimit("key-1", 42).Should().Be(42);
    }
}
