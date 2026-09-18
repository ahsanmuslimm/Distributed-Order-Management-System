namespace Orders.Service.DTOs;

/// <summary>
/// Request DTO for placing a new order
/// Represents the input from the HTTP client
/// </summary>
public record OrderRequest
{
    /// <summary>
    /// Customer ID placing the order
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Line items in the order
    /// Must have at least one item
    /// </summary>
    public required OrderItemRequest[] Items { get; init; }
}

/// <summary>
/// Represents a line item in an order request
/// </summary>
public record OrderItemRequest
{
    /// <summary>
    /// Product ID to order
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Quantity to order
    /// Must be positive
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>
    /// Unit price at time of order
    /// Used for invoice/history
    /// </summary>
    public decimal UnitPrice { get; init; }
}
