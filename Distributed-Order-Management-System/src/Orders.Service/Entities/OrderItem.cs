namespace Orders.Service.Entities;

/// <summary>
/// Represents a line item in an order
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Unique identifier for this order item (PK)
    /// </summary>
    public Guid OrderItemId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the parent order (FK)
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// The product being ordered
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Number of units ordered
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Price per unit at time of order (immutable)
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total line amount (Quantity * UnitPrice)
    /// </summary>
    public decimal LineTotal => Quantity * UnitPrice;

    /// <summary>
    /// Navigation property to parent order
    /// </summary>
    public virtual Order? Order { get; set; }
}
