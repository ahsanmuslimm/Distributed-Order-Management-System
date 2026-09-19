using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Orders.Service.DTOs;
using Orders.Service.Handlers;
using Orders.Service.Infrastructure;

namespace Orders.Service.Endpoints;

/// <summary>
/// HTTP endpoint: GET /api/orders/{orderId}
/// 
/// Retrieves the current status of an order including:
/// - Basic order information
/// - Line items
/// - Status history (audit trail)
/// </summary>
public static class GetOrderStatusEndpoint
{
    /// <summary>
    /// HTTP handler
    /// </summary>
    public static async Task<Results<Ok<OrderResponse>, NotFound, StatusCodeHttpResult>> Handle(
        Guid orderId,
        GetOrderStatusHandler handler,
        HttpContext httpContext)
    {
        // Extract or generate Correlation ID
        var correlationId = httpContext.Request.ExtractOrGenerateCorrelationId();
        CorrelationIdContext.Set(correlationId);

        try
        {
            // Validate input
            if (orderId == Guid.Empty)
                return TypedResults.NotFound();

            // Handle the request
            var order = await handler.HandleAsync(orderId, correlationId);

            // Add Correlation ID to response header
            httpContext.Response.AddCorrelationIdHeader(correlationId);

            if (order == null)
                return TypedResults.NotFound();

            // Return 200 OK with order
            return TypedResults.Ok(order);
        }
        catch (Exception ex)
        {
            httpContext.Response.AddCorrelationIdHeader(correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
        finally
        {
            CorrelationIdContext.Clear();
        }
    }

    /// <summary>
    /// Register this endpoint in the application
    /// </summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/orders/{orderId}", Handle)
            .WithName("GetOrderStatus")
            .WithOpenApi()
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithDescription("Get order status and history")
            .WithSummary("Query order");
    }
}
