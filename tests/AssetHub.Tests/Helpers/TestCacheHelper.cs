using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Creates a <see cref="HybridCache"/> instance backed by in-memory distributed cache only.
/// Suitable for unit/integration tests that need a real HybridCache without Redis.
/// </summary>
public static class TestCacheHelper
{
    public static HybridCache CreateHybridCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HybridCache>();
    }
}
