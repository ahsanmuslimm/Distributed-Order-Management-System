using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Payment.Service.Endpoints;

/// <summary>
/// HTTP endpoint: POST /api/payments/charge
/// 
/// Charges payment for an order
/// </summary>
public class ChargePaymentEndpoint
{
    private readonly ILogger<ChargePaymentEndpoint> _logger;

    public ChargePaymentEndpoint(ILogger<ChargePaymentEndpoint> logger)
    {
        _logger = logger;
    }

    public async Task<Results<Ok, BadRequest<string>, StatusCodeHttpResult>> Handle(
        [FromBody] ChargeRequest request)
    {
        try
        {
            _logger.LogInformation("Charging payment for order {OrderId}, amount {Amount}", 
                request.OrderId, request.Amount);
            
            // For now, just accept all payments (mock)
            // In a real implementation, would call payment processor
            
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error charging payment for order {OrderId}", request.OrderId);
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/payments/charge", (ChargeRequest request, ChargePaymentEndpoint endpoint) => endpoint.Handle(request))
            .WithName("ChargePayment")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status500InternalServerError);
    }
}

public record ChargeRequest
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal Amount { get; init; }
}
