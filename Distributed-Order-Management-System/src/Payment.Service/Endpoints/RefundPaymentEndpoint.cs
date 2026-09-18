using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Payment.Service.Endpoints;

/// <summary>
/// HTTP endpoint: POST /api/payments/refund
/// 
/// Refunds payment (compensation)
/// </summary>
public class RefundPaymentEndpoint
{
    private readonly ILogger<RefundPaymentEndpoint> _logger;

    public RefundPaymentEndpoint(ILogger<RefundPaymentEndpoint> logger)
    {
        _logger = logger;
    }

    public async Task<Results<Ok, BadRequest<string>, StatusCodeHttpResult>> Handle(
        [FromBody] RefundRequest request)
    {
        try
        {
            _logger.LogInformation("Refunding payment for order {OrderId}, amount {Amount}", 
                request.OrderId, request.Amount);
            
            // For now, just accept all refunds (mock)
            
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refunding payment for order {OrderId}", request.OrderId);
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/payments/refund", (RefundRequest request, RefundPaymentEndpoint endpoint) => endpoint.Handle(request))
            .WithName("RefundPayment")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);
    }
}

public record RefundRequest
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
}
