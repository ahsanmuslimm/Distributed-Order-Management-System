namespace Orders.Service.Entities;

/// <summary>
/// Order Aggregate Root
/// Represents a customer's order with items and status tracking
/// </summary>
public class Order
{
    /// <summary>
    /// Unique order identifier (PK)
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Customer who placed the order
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Current order status (Pending|Reserved|Charged|Confirmed|Failed|Compensated)
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Total amount for the order (sum of Items)
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Reference to the saga instance managing this order
    /// </summary>
    public Guid? SagaId { get; set; }

    /// <summary>
    /// When the order was created (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the order was last modified (UTC)
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic concurrency control version
    /// Incremented with each update
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Collection of items in this order
    /// </summary>
    public virtual List<OrderItem> Items { get; set; } = new();

    /// <summary>
    /// Audit trail of status transitions
    /// </summary>
    public virtual List<OrderStatusTransition> StatusTransitions { get; set; } = new();
}

/// <summary>
/// Order status enumeration
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order created, awaiting inventory reservation
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Inventory reserved, awaiting payment
    /// </summary>
    Reserved = 1,

    /// <summary>
    /// Payment charged successfully, awaiting confirmation
    /// </summary>
    Charged = 2,

    /// <summary>
    /// Order confirmed and complete
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// Order failed (inventory unavailable, payment failed, etc.)
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Order was confirmed but then compensation was applied (e.g., due to orchestrator failure)
    /// </summary>
    Compensated = 5
}
