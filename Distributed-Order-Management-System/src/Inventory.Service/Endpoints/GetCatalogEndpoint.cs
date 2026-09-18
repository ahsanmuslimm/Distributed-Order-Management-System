using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Inventory.Service.Data;
using Inventory.Service.Infrastructure;

namespace Inventory.Service.Endpoints;

/// <summary>
/// HTTP endpoint: GET /api/catalog
/// 
/// Returns list of all products with cache-aside pattern:
/// 1. Check Redis for "catalog" key
/// 2. If hit: Return cached products
/// 3. If miss: Query Postgres, cache with TTL (60s), return
/// 4. If Redis down: Fall back to direct Postgres query
/// 
/// Why cache the catalog?
/// - Catalog changes rarely (maybe once per day)
/// - Queried frequently (on every frontend page load)
/// - Reducing DB load improves response time
/// 
/// Why NOT cache stock levels?
/// - Stock changes frequently (with every order)
/// - Stale cache would lead to overbooking
/// - Better to calculate from ledger for accuracy
/// </summary>
public static class GetCatalogEndpoint
{
    /// <summary>
    /// HTTP handler
    /// </summary>
    public static async Task<Results<Ok<ProductCatalogResponse>, StatusCodeHttpResult>> Handle(
        [FromServices] InventoryDbContext dbContext,
        [FromServices] ICatalogCache catalogCache,
        HttpContext httpContext)
    {
        var correlationId = httpContext.Request.ExtractOrGenerateCorrelationId();

        try
        {
            // Try to get from cache
            var cached = await catalogCache.GetCatalogAsync();
            if (cached != null && cached.Products.Length > 0)
            {
                httpContext.Response.AddCorrelationIdHeader(correlationId);
                httpContext.Response.Headers.Add("X-Cache-Source", "Redis");
                
                return TypedResults.Ok(new ProductCatalogResponse
                {
                    Products = cached.Products,
                    CachedAt = cached.CachedAt,
                    IsFromCache = true
                });
            }

            // Cache miss - query database
            var products = await dbContext.Products
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

            // Cache the result
            if (products.Count > 0)
            {
                await catalogCache.SetCatalogAsync(new CatalogCacheEntry
                {
                    Products = products.ToArray(),
                    CachedAt = DateTime.UtcNow
                });
            }

            httpContext.Response.AddCorrelationIdHeader(correlationId);
            httpContext.Response.Headers.Add("X-Cache-Source", "Postgres");

            return TypedResults.Ok(new ProductCatalogResponse
            {
                Products = products.ToArray(),
                CachedAt = DateTime.UtcNow,
                IsFromCache = false
            });
        }
        catch (Exception ex)
        {
            httpContext.Response.AddCorrelationIdHeader(correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Register this endpoint in the application
    /// </summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/catalog", Handle)
            .WithName("GetCatalog")
            .WithOpenApi()
            .Produces<ProductCatalogResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithDescription("Get all products in catalog (cached)")
            .WithSummary("List products");
    }
}

/// <summary>
/// Data transfer object for product in catalog
/// </summary>
public record ProductDto
{
    /// <summary>
    /// Unique product ID
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Product name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Product description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Product price
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// When product was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Response for get catalog endpoint
/// </summary>
public record ProductCatalogResponse
{
    /// <summary>
    /// List of all products
    /// </summary>
    public required ProductDto[] Products { get; init; }

    /// <summary>
    /// When this data was cached (or queried)
    /// </summary>
    public required DateTime CachedAt { get; init; }

    /// <summary>
    /// Whether data came from cache (true) or database (false)
    /// </summary>
    public required bool IsFromCache { get; init; }
}

/// <summary>
/// Cache entry for catalog
/// </summary>
public record CatalogCacheEntry
{
    /// <summary>
    /// Array of products
    /// </summary>
    public required ProductDto[] Products { get; init; }

    /// <summary>
    /// When this entry was cached
    /// </summary>
    public required DateTime CachedAt { get; init; }
}

/// <summary>
/// Interface for catalog cache operations
/// </summary>
public interface ICatalogCache
{
    /// <summary>
    /// Get catalog from cache
    /// </summary>
    Task<CatalogCacheEntry?> GetCatalogAsync();

    /// <summary>
    /// Set catalog in cache with TTL
    /// </summary>
    Task SetCatalogAsync(CatalogCacheEntry entry);

    /// <summary>
    /// Invalidate catalog cache
    /// </summary>
    Task InvalidateCatalogAsync();
}

