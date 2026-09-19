namespace Contracts.Events;

/// <summary>
/// Base class for all domain events
/// All events carry MessageId (idempotency) and CorrelationId (tracing)
/// </summary>
public abstract record BaseEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Order Service Events
/// </summary>
public record OrderPlacedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required OrderItem[] Items { get; init; }
}

public record OrderConfirmedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
}

public record OrderFailedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Inventory Service Events
/// </summary>
public record InventoryReservedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
}

public record InventoryRejectedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int RequestedQuantity { get; init; }
    public required string Reason { get; init; }
}

public record InventoryReleasedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>
/// Payment Service Events
/// </summary>
public record PaymentChargedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required string TransactionId { get; init; }
}

public record PaymentFailedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Reason { get; init; }
}

public record PaymentRefundedEvent : BaseEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required string RefundId { get; init; }
}

/// <summary>
/// Helper classes
/// </summary>
public record OrderItem
{
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
