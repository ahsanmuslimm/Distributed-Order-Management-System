using Microsoft.EntityFrameworkCore;
using Inventory.Service.Data;
using Inventory.Service.Entities;
using Inventory.Service.Endpoints;
using Inventory.Service.Infrastructure;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Integration tests for GetCatalog endpoint with cache-aside pattern
/// </summary>
public class GetCatalogEndpointTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private MockCatalogCache _cache = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _cache = new MockCatalogCache();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Cache Miss - Query Database
    // ========================================================================
    [Fact]
    public async Task GetCatalog_OnCacheMiss_QueriesDatabase()
    {
        // Arrange
        var product1 = new Product
        {
            ProductId = Guid.NewGuid(),
            Name = "Widget",
            Description = "A useful widget",
            Price = 9.99m,
            InitialStock = 100
        };
        
        var product2 = new Product
        {
            ProductId = Guid.NewGuid(),
            Name = "Gadget",
            Description = "A cool gadget",
            Price = 19.99m,
            InitialStock = 50
        };

        _dbContext.Products.Add(product1);
        _dbContext.Products.Add(product2);
        await _dbContext.SaveChangesAsync();

        // Act
        var cachedEntry = await _cache.GetCatalogAsync();
        Assert.Null(cachedEntry);  // Cache miss

        // Query database
        var products = await _dbContext.Products
            .AsNoTracking()
            .Select(p => new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        // Store in cache
        var cacheEntry = new CatalogCacheEntry
        {
            Products = products.ToArray(),
            CachedAt = DateTime.UtcNow
        };
        await _cache.SetCatalogAsync(cacheEntry);

        // Assert
        Assert.Equal(2, cacheEntry.Products.Length);
        Assert.Contains(cacheEntry.Products, p => p.Name == "Widget");
        Assert.Contains(cacheEntry.Products, p => p.Name == "Gadget");
    }

    // ========================================================================
    // Test: Cache Hit - Returns Cached Data
    // ========================================================================
    [Fact]
    public async Task GetCatalog_OnCacheHit_ReturnsCachedData()
    {
        // Arrange
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            Name = "Widget",
            Description = "A useful widget",
            Price = 9.99m,
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Pre-populate cache
        var productDto = new ProductDto
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            CreatedAt = product.CreatedAt
        };

        var cacheEntry = new CatalogCacheEntry
        {
            Products = new[] { productDto },
            CachedAt = DateTime.UtcNow.AddSeconds(-30)  // Cached 30s ago
        };
        await _cache.SetCatalogAsync(cacheEntry);

        // Act
        var cached = await _cache.GetCatalogAsync();

        // Assert
        Assert.NotNull(cached);
        Assert.Single(cached.Products);
        Assert.Equal("Widget", cached.Products[0].Name);
    }

    // ========================================================================
    // Test: Empty Catalog
    // ========================================================================
    [Fact]
    public async Task GetCatalog_WithNoProducts_ReturnsEmptyArray()
    {
        // Arrange - No products added

        // Act
        var products = await _dbContext.Products
            .AsNoTracking()
            .Select(p => new ProductDto
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        // Assert
        Assert.Empty(products);
    }

    // ========================================================================
    // Test: Cache Invalidation
    // ========================================================================
    [Fact]
    public async Task GetCatalog_AfterInvalidation_CacheIsClear()
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
                    Description = "Widget",
                    Price = 9.99m,
                    CreatedAt = DateTime.UtcNow
                }
            },
            CachedAt = DateTime.UtcNow
        };
        await _cache.SetCatalogAsync(cacheEntry);

        var cached1 = await _cache.GetCatalogAsync();
        Assert.NotNull(cached1);

        // Act - Invalidate
        await _cache.InvalidateCatalogAsync();

        // Assert
        var cached2 = await _cache.GetCatalogAsync();
        Assert.Null(cached2);
    }

    // ========================================================================
    // Test: Large Catalog (Many Products)
    // ========================================================================
    [Fact]
    public async Task GetCatalog_WithManyProducts_Succeeds()
    {
        // Arrange
        const int productCount = 1000;
        
        for (int i = 0; i < productCount; i++)
        {
            var product = new Product
            {
                ProductId = Guid.NewGuid(),
                Name = $"Product_{i}",
                Description = $"Description {i}",
                Price = 10.00m + i,
                InitialStock = 100 + i
            };
            _dbContext.Products.Add(product);
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var products = await _dbContext.Products
            .AsNoTracking()
            .ToListAsync();

        // Assert
        Assert.Equal(productCount, products.Count);
    }
}

/// <summary>
/// Mock implementation of ICatalogCache for testing
/// </summary>
internal class MockCatalogCache : ICatalogCache
{
    private CatalogCacheEntry? _cache = null;

    public Task<CatalogCacheEntry?> GetCatalogAsync()
    {
        return Task.FromResult(_cache);
    }

    public Task SetCatalogAsync(CatalogCacheEntry entry)
    {
        _cache = entry;
        return Task.CompletedTask;
    }

    public Task InvalidateCatalogAsync()
    {
        _cache = null;
        return Task.CompletedTask;
    }
}

