using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Service.Endpoints;

/// <summary>
/// HTTP endpoint: POST /api/inventory/release
/// 
/// Releases reserved inventory (compensation)
/// </summary>
public class ReleaseInventoryEndpoint
{
    private readonly ILogger<ReleaseInventoryEndpoint> _logger;

    public ReleaseInventoryEndpoint(ILogger<ReleaseInventoryEndpoint> logger)
    {
        _logger = logger;
    }

    public async Task<Results<Ok, BadRequest<string>, StatusCodeHttpResult>> Handle(
        [FromBody] ReleaseRequest request)
    {
        try
        {
            _logger.LogInformation("Releasing inventory for order {OrderId}", request.OrderId);
            
            // For now, just accept all releases (mock)
            
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing inventory for order {OrderId}", request.OrderId);
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/inventory/release", (ReleaseRequest request, ReleaseInventoryEndpoint endpoint) => endpoint.Handle(request))
            .WithName("ReleaseInventory")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);
    }
}

public record ReleaseRequest
{
    public Guid OrderId { get; init; }
    public required ReleaseItem[] Items { get; init; }
}

public record ReleaseItem
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}
