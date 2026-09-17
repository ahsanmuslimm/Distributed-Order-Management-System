namespace Orders.Service.Entities;

/// <summary>
/// Audit trail entry for order status changes
/// Immutable record of all transitions for debugging and compliance
/// </summary>
public class OrderStatusTransition
{
    /// <summary>
    /// Unique identifier for this transition record (PK)
    /// </summary>
    public Guid TransitionId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The order being transitioned (FK)
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Previous status (nullable for initial transition)
    /// </summary>
    public OrderStatus? FromStatus { get; set; }

    /// <summary>
    /// New status after transition
    /// </summary>
    public OrderStatus ToStatus { get; set; }

    /// <summary>
    /// Reason for transition (e.g., "Inventory reserved", "Payment failed")
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the transition occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID linking this transition to the saga/request
    /// Used for distributed tracing
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Navigation property to parent order
    /// </summary>
    public virtual Order? Order { get; set; }
}
