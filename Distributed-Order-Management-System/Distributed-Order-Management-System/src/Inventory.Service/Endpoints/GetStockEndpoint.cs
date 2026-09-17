using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Inventory.Service.Domain;
using Inventory.Service.Infrastructure;

namespace Inventory.Service.Endpoints;

/// <summary>
/// HTTP endpoint: GET /api/products/{id}/stock
/// 
/// Returns current stock for a specific product.
/// 
/// IMPORTANT: This endpoint does NOT use cache.
/// 
/// Why no cache?
/// - Stock is the source of truth for order fulfillment
/// - Stale stock = overbooking = order failure
/// - Better to have slightly slower accurate response than fast wrong response
/// - Cache is only safe for catalog (products rarely change)
/// 
/// Stock is calculated in real-time from immutable ledger:
/// CurrentStock = InitialStock - (sum of reserves) + (sum of releases)
/// </summary>
public static class GetStockEndpoint
{
    /// <summary>
    /// HTTP handler
    /// </summary>
    public static async Task<Results<Ok<ProductStockResponse>, NotFound, StatusCodeHttpResult>> Handle(
        [FromRoute] Guid id,
        [FromServices] IStockCalculator stockCalculator,
        HttpContext httpContext)
    {
        var correlationId = httpContext.Request.ExtractOrGenerateCorrelationId();

        try
        {
            // Validate product ID
            if (id == Guid.Empty)
            {
                httpContext.Response.AddCorrelationIdHeader(correlationId);
                return TypedResults.NotFound();
            }

            // Calculate current stock from ledger
            var currentStock = await stockCalculator.CalculateStockAsync(id);

            // If product not found, stock will be 0
            // (We could differentiate "not found" vs "out of stock", but for now treat the same)

            httpContext.Response.AddCorrelationIdHeader(correlationId);

            return TypedResults.Ok(new ProductStockResponse
            {
                ProductId = id,
                CurrentStock = currentStock,
                CalculatedAt = DateTime.UtcNow
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
        app.MapGet("/api/products/{id:guid}/stock", Handle)
            .WithName("GetProductStock")
            .WithOpenApi()
            .Produces<ProductStockResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithDescription("Get current stock for a product (real-time from ledger)")
            .WithSummary("Get product stock");
    }
}

/// <summary>
/// Response for get stock endpoint
/// </summary>
public record ProductStockResponse
{
    /// <summary>
    /// Product ID
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Current available stock (calculated from ledger)
    /// </summary>
    public required int CurrentStock { get; init; }

    /// <summary>
    /// When stock was calculated
    /// </summary>
    public required DateTime CalculatedAt { get; init; }
}

