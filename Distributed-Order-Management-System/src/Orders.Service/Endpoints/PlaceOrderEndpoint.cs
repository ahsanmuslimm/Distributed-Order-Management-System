using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Orders.Service.DTOs;
using Orders.Service.Handlers;
using Orders.Service.Infrastructure;

namespace Orders.Service.Endpoints;

/// <summary>
/// HTTP endpoint: POST /api/orders
/// 
/// Places a new order and returns 202 Accepted
/// The order is created synchronously, but further processing (inventory, payment)
/// happens asynchronously through the saga orchestrator.
/// </summary>
public static class PlaceOrderEndpoint
{
    /// <summary>
    /// HTTP handler
    /// </summary>
    public static async Task<Results<AcceptedAtRoute<PlaceOrderResponse>, BadRequest<string>, StatusCodeHttpResult>> Handle(
        [FromBody] OrderRequest request,
        [FromServices] PlaceOrderHandler handler,
        HttpContext httpContext)
    {
        // Extract or generate Correlation ID
        var correlationId = httpContext.Request.ExtractOrGenerateCorrelationId();
        CorrelationIdContext.Set(correlationId);

        try
        {
            // Validate request
            if (request == null)
                return TypedResults.BadRequest("Order request is required");

            if (request.CustomerId == Guid.Empty)
                return TypedResults.BadRequest("CustomerId cannot be empty");

            if (request.Items == null || request.Items.Length == 0)
                return TypedResults.BadRequest("Order must have at least one item");

            foreach (var item in request.Items)
            {
                if (item.ProductId == Guid.Empty)
                    return TypedResults.BadRequest("All items must have a ProductId");

                if (item.Quantity <= 0)
                    return TypedResults.BadRequest("All items must have positive Quantity");

                if (item.UnitPrice < 0)
                    return TypedResults.BadRequest("UnitPrice cannot be negative");
            }

            // Handle the request
            var result = await handler.HandleAsync(request, correlationId);

            // Create response
            var response = new PlaceOrderResponse
            {
                OrderId = result.OrderId,
                CustomerId = result.CustomerId,
                Status = result.Status,
                TotalAmount = result.TotalAmount,
                CreatedAt = result.CreatedAt,
                ItemCount = result.ItemCount
            };

            // Add Correlation ID to response header
            httpContext.Response.AddCorrelationIdHeader(correlationId);

            // Return 202 Accepted (order accepted, processing asynchronously)
            return TypedResults.AcceptedAtRoute(
                routeName: "GetOrderStatus",
                routeValues: new { orderId = result.OrderId },
                value: response);
        }
        catch (ArgumentException ex)
        {
            httpContext.Response.AddCorrelationIdHeader(correlationId);
            return TypedResults.BadRequest(ex.Message);
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
        app.MapPost("/api/orders", Handle)
            .WithName("PlaceOrder")
            .WithOpenApi()
            .Produces<PlaceOrderResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithDescription("Place a new order")
            .WithSummary("Create order");
    }
}

/// <summary>
/// Response for place order endpoint
/// </summary>
public record PlaceOrderResponse
{
    /// <summary>
    /// New order ID
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Customer ID
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Initial status (always Pending)
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Total order amount
    /// </summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>
    /// When order was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Number of line items
    /// </summary>
    public required int ItemCount { get; init; }
}
