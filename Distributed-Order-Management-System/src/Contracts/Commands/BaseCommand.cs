namespace Contracts.Commands;

/// <summary>
/// Base class for all commands
/// Commands are imperative and routed only from Saga Orchestrator to services
/// </summary>
public abstract record BaseCommand
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Inventory Service Commands (from Saga Orchestrator)
/// </summary>
public record ReserveInventoryCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
}

public record ReleaseInventoryCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>
/// Payment Service Commands (from Saga Orchestrator)
/// </summary>
public record ChargePaymentCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public decimal FailureRate { get; init; } = 0.0m; // For testing failure injection
}

public record RefundPaymentCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required decimal Amount { get; init; }
    public string Reason { get; init; } = "Saga compensation";
}

/// <summary>
/// Order Service Commands (from Saga Orchestrator)
/// </summary>
public record ConfirmOrderCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
}

public record FailOrderCommand : BaseCommand
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string Reason { get; init; }
}
