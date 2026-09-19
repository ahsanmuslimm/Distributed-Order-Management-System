using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Inventory.Service.Endpoints;

namespace Inventory.Service.Infrastructure;

/// <summary>
/// Redis implementation of catalog cache
/// 
/// Cache-Aside Pattern:
/// 1. Request comes in
/// 2. Try GET "catalog" from Redis
/// 3. If HIT: Return cached data (very fast, ~1ms)
/// 4. If MISS: Query Postgres, SET "catalog" with TTL, return
/// 5. If Redis is down: Skip caching, query Postgres directly
/// 
/// Why Redis TTL = 60 seconds?
/// - Catalog rarely changes (new products added max few times/day)
/// - 60 second stale window is acceptable
/// - If product added, cache automatically expires after 60s
/// - Admin can manually invalidate if needed
/// </summary>
public interface IRedisCatalogCache : ICatalogCache
{
    /// <summary>
    /// Check if Redis is healthy
    /// </summary>
    Task<bool> IsHealthyAsync();
}

/// <summary>
/// Redis catalog cache implementation using StackExchange.Redis
/// </summary>
public class RedisCatalogCache : IRedisCatalogCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCatalogCache> _logger;
    private const string CatalogCacheKey = "catalog";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public RedisCatalogCache(IConnectionMultiplexer redis, ILogger<RedisCatalogCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Get catalog from cache
    /// </summary>
    public async Task<CatalogCacheEntry?> GetCatalogAsync()
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Redis not connected, skipping cache GET");
                return null;
            }

            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(CatalogCacheKey);

            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("Catalog cache MISS");
                return null;
            }

            var entry = JsonSerializer.Deserialize<CatalogCacheEntry>(cached.ToString());
            _logger.LogInformation("Catalog cache HIT, {ProductCount} products", entry?.Products.Length ?? 0);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from Redis catalog cache");
            return null;  // Graceful fallback
        }
    }

    /// <summary>
    /// Set catalog in cache with TTL
    /// </summary>
    public async Task SetCatalogAsync(CatalogCacheEntry entry)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Redis not connected, skipping cache SET");
                return;
            }

            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(entry);
            
            await db.StringSetAsync(CatalogCacheKey, json, CacheTtl);
            _logger.LogInformation("Catalog cached, TTL: {TtlSeconds}s, ProductCount: {ProductCount}", 
                CacheTtl.TotalSeconds, entry.Products.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to Redis catalog cache");
            // Don't throw - caching is optional, service should work without Redis
        }
    }

    /// <summary>
    /// Invalidate catalog cache
    /// </summary>
    public async Task InvalidateCatalogAsync()
    {
        try
        {
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Redis not connected, skipping cache invalidation");
                return;
            }

            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(CatalogCacheKey);
            _logger.LogInformation("Catalog cache invalidated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating Redis catalog cache");
        }
    }

    /// <summary>
    /// Check if Redis is healthy
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            if (!_redis.IsConnected)
                return false;

            var db = _redis.GetDatabase();
            var result = await db.PingAsync();
            return result != TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health check failed");
            return false;
        }
    }
}

/// <summary>
/// No-op implementation for when Redis is disabled or unavailable
/// </summary>
public class NoCatalogCache : ICatalogCache
{
    private readonly ILogger<NoCatalogCache> _logger;

    public NoCatalogCache(ILogger<NoCatalogCache> logger)
    {
        _logger = logger;
    }

    public Task<CatalogCacheEntry?> GetCatalogAsync()
    {
        _logger.LogDebug("No-op cache: returning null (cache disabled)");
        return Task.FromResult<CatalogCacheEntry?>(null);
    }

    public async Task SetCatalogAsync(CatalogCacheEntry entry)
    {
        _logger.LogDebug("No-op cache: ignoring SET (cache disabled)");
        await Task.CompletedTask;
    }

    public async Task InvalidateCatalogAsync()
    {
        _logger.LogDebug("No-op cache: ignoring invalidation (cache disabled)");
        await Task.CompletedTask;
    }
}

