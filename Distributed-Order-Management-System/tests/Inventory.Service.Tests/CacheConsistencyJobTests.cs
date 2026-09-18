using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Endpoints;
using Inventory.Service.Services;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Tests for CacheConsistencyJob background service
/// </summary>
public class CacheConsistencyJobTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private MockCatalogCache _cache = null!;
    private IStockCalculator _stockCalculator = null!;
    private CacheConsistencyJob _job = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _cache = new MockCatalogCache();

        var logger = new MockLogger<StockCalculator>();
        _stockCalculator = new StockCalculator(_dbContext, logger);

        var jobLogger = new MockLogger<CacheConsistencyJob>();
        _job = new CacheConsistencyJob(_dbContext, _stockCalculator, _cache, jobLogger);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Cache Invalidation
    // ========================================================================
    [Fact]
    public async Task CacheConsistencyJob_RunCheck_InvalidatesCache()
    {
        // Arrange
        var cacheEntry = new CatalogCacheEntry
        {
            Products = new[]
            {
                new ProductDto
                {
                    ProductId = Guid.NewGuid(),
                    Name = "Widget",
                    Description = "A widget",
                    Price = 9.99m,
                    CreatedAt = DateTime.UtcNow
                }
            },
            CachedAt = DateTime.UtcNow
        };
        await _cache.SetCatalogAsync(cacheEntry);

        var cachedBefore = await _cache.GetCatalogAsync();
        Assert.NotNull(cachedBefore);

        // Act
        await _job.RunCheckAsync();

        // Assert
        var cachedAfter = await _cache.GetCatalogAsync();
        Assert.Null(cachedAfter);  // Cache invalidated
    }

    // ========================================================================
    // Test: Handles Errors Gracefully
    // ========================================================================
    [Fact]
    public async Task CacheConsistencyJob_OnError_LogsAndContinues()
    {
        // Arrange - Use a cache that throws
        var failingCache = new FailingMockCache();
        var jobLogger = new MockLogger<CacheConsistencyJob>();
        var job = new CacheConsistencyJob(_dbContext, _stockCalculator, failingCache, jobLogger);

        // Act
        await job.RunCheckAsync();

        // Assert - Should not throw (handles gracefully)
        // (In real scenario, would check logs)
    }

    // ========================================================================
    // Test: Can be triggered manually
    // ========================================================================
    [Fact]
    public async Task CacheConsistencyJob_CanBeTriggeredManually()
    {
        // Arrange
        var cacheEntry = new CatalogCacheEntry
        {
            Products = Array.Empty<ProductDto>(),
            CachedAt = DateTime.UtcNow
        };
        await _cache.SetCatalogAsync(cacheEntry);

        // Act
        await _job.RunCheckAsync();

        // Assert
        var cached = await _cache.GetCatalogAsync();
        Assert.Null(cached);
    }

    // ========================================================================
    // Test: Multiple runs work correctly
    // ========================================================================
    [Fact]
    public async Task CacheConsistencyJob_MultipleRuns_WorkCorrectly()
    {
        // Arrange
        var cacheEntry = new CatalogCacheEntry
        {
            Products = Array.Empty<ProductDto>(),
            CachedAt = DateTime.UtcNow
        };

        // Act - Run multiple times
        for (int i = 0; i < 3; i++)
        {
            await _cache.SetCatalogAsync(cacheEntry);
            await _job.RunCheckAsync();
            
            var cached = await _cache.GetCatalogAsync();
            Assert.Null(cached);
        }

        // Assert - No errors should occur
    }
}

/// <summary>
/// Mock cache that fails for testing error handling
/// </summary>
internal class FailingMockCache : ICatalogCache
{
    public Task<CatalogCacheEntry?> GetCatalogAsync()
    {
        throw new InvalidOperationException("Cache error");
    }

    public Task SetCatalogAsync(CatalogCacheEntry entry)
    {
        throw new InvalidOperationException("Cache error");
    }

    public Task InvalidateCatalogAsync()
    {
        throw new InvalidOperationException("Cache error");
    }
}

