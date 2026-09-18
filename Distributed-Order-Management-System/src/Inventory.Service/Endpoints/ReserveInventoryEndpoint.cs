using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Inventory.Service.Handlers;

namespace Inventory.Service.Endpoints;

/// <summary>
/// HTTP endpoint: POST /api/inventory/reserve
/// 
/// Reserves inventory for an order
/// </summary>
public class ReserveInventoryEndpoint
{
    private readonly ILogger<ReserveInventoryEndpoint> _logger;

    public ReserveInventoryEndpoint(ILogger<ReserveInventoryEndpoint> logger)
    {
        _logger = logger;
    }

    public async Task<Results<Ok, BadRequest<string>, StatusCodeHttpResult>> Handle(
        [FromBody] ReserveRequest request)
    {
        try
        {
            _logger.LogInformation("Reserving inventory for order {OrderId}", request.OrderId);
            
            // For now, just accept all reservations (mock)
            // In a real implementation, would check stock and reserve atomically
            
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving inventory for order {OrderId}", request.OrderId);
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/inventory/reserve", (ReserveRequest request, ReserveInventoryEndpoint endpoint) => endpoint.Handle(request))
            .WithName("ReserveInventory")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);
    }
}

public record ReserveRequest
{
    public Guid OrderId { get; init; }
    public required ReserveItem[] Items { get; init; }
}

public record ReserveItem
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}
