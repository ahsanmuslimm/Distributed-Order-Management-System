using Orders.Service.Entities;

namespace Orders.Service.DTOs;

/// <summary>
/// Response DTO for order queries
/// Represents the current state of an order
/// </summary>
public record OrderResponse
{
    /// <summary>
    /// Order ID
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Customer who placed the order
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Current status
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Total amount for the order
    /// </summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>
    /// When the order was created (UTC)
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the order was last updated (UTC)
    /// </summary>
    public required DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Saga ID managing this order (if assigned)
    /// </summary>
    public Guid? SagaId { get; init; }

    /// <summary>
    /// Line items in the order
    /// </summary>
    public required OrderItemResponse[] Items { get; init; }

    /// <summary>
    /// Status transition history
    /// </summary>
    public required OrderStatusTransitionResponse[] StatusHistory { get; init; }

    /// <summary>
    /// Map from Order entity to response DTO
    /// </summary>
    public static OrderResponse FromEntity(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            Status = order.Status.ToString(),
            TotalAmount = order.TotalAmount,
            CreatedAt = order.CreatedAt,
            LastUpdatedAt = order.LastUpdatedAt,
            SagaId = order.SagaId,
            Items = order.Items
                .Select(oi => new OrderItemResponse
                {
                    OrderItemId = oi.OrderItemId,
                    ProductId = oi.ProductId,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    LineTotal = oi.LineTotal
                })
                .ToArray(),
            StatusHistory = order.StatusTransitions
                .OrderBy(st => st.Timestamp)
                .Select(st => new OrderStatusTransitionResponse
                {
                    TransitionId = st.TransitionId,
                    FromStatus = st.FromStatus?.ToString(),
                    ToStatus = st.ToStatus.ToString(),
                    Reason = st.Reason,
                    Timestamp = st.Timestamp,
                    CorrelationId = st.CorrelationId
                })
                .ToArray()
        };
    }
}

/// <summary>
/// Line item in order response
/// </summary>
public record OrderItemResponse
{
    /// <summary>
    /// Order item ID
    /// </summary>
    public required Guid OrderItemId { get; init; }

    /// <summary>
    /// Product ID
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Quantity ordered
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>
    /// Unit price at time of order
    /// </summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>
    /// Total line amount (Quantity * UnitPrice)
    /// </summary>
    public required decimal LineTotal { get; init; }
}

/// <summary>
/// Status transition in history
/// </summary>
public record OrderStatusTransitionResponse
{
    /// <summary>
    /// Transition record ID
    /// </summary>
    public required Guid TransitionId { get; init; }

    /// <summary>
    /// Previous status
    /// </summary>
    public string? FromStatus { get; init; }

    /// <summary>
    /// New status
    /// </summary>
    public required string ToStatus { get; init; }

    /// <summary>
    /// Reason for transition
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// When transition occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Correlation ID for tracing
    /// </summary>
    public required Guid CorrelationId { get; init; }
}
