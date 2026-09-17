using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orders.Service.Data;
using Orders.Service.DTOs;
using Orders.Service.Entities;

namespace Orders.Service.Handlers;

/// <summary>
/// Application handler for placing a new order
/// 
/// Responsibilities:
/// 1. Validate order request
/// 2. Create Order and OrderItems entities
/// 3. Store in database
/// 4. Return order ID for saga coordination
/// 
/// This handler does NOT call other services directly.
/// It publishes an OrderPlacedEvent for the saga to consume.
/// </summary>
public class PlaceOrderHandler
{
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<PlaceOrderHandler> _logger;

    public PlaceOrderHandler(OrderDbContext dbContext, ILogger<PlaceOrderHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Handle the place order request
    /// 
    /// Process:
    /// 1. Validate input
    /// 2. Create Order aggregate (with items)
    /// 3. Calculate total amount
    /// 4. Save to database (atomically)
    /// 5. Record status transition (Pending)
    /// 6. Return order ID (caller publishes event)
    /// </summary>
    /// <param name="request">Order request from HTTP endpoint</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <returns>Created order ID and details</returns>
    public async Task<PlaceOrderResult> HandleAsync(
        OrderRequest request,
        Guid correlationId)
    {
        // Validate input
        ValidateRequest(request);

        // Create order ID (should use deterministic ID if from saga, but here we generate)
        var orderId = Guid.NewGuid();

        // Create order items from request
        var orderItems = request.Items.Select(item => new OrderItem
        {
            OrderItemId = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice
        }).ToList();

        // Calculate total amount
        var totalAmount = orderItems.Sum(oi => oi.LineTotal);

        // Create order entity
        var order = new Order
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Status = OrderStatus.Pending,
            TotalAmount = totalAmount,
            Items = orderItems,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Create initial status transition (audit trail)
        var initialTransition = new OrderStatusTransition
        {
            TransitionId = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = null,  // No previous status
            ToStatus = OrderStatus.Pending,
            Reason = "Order created",
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId
        };

        // Use transaction to ensure atomicity
        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // Add order and items to context
                _dbContext.Orders.Add(order);
                _dbContext.OrderStatusTransitions.Add(initialTransition);

                // Save to database
                await _dbContext.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Order placed successfully. OrderId: {OrderId}, CustomerId: {CustomerId}, " +
                    "TotalAmount: {TotalAmount}, Items: {ItemCount}, CorrelationId: {CorrelationId}",
                    orderId, request.CustomerId, totalAmount, orderItems.Count, correlationId);

                return new PlaceOrderResult
                {
                    OrderId = orderId,
                    CustomerId = request.CustomerId,
                    Status = order.Status.ToString(),
                    TotalAmount = totalAmount,
                    CreatedAt = order.CreatedAt,
                    ItemCount = orderItems.Count
                };
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error while placing order. OrderId: {OrderId}, CorrelationId: {CorrelationId}",
                    orderId, correlationId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error while placing order. OrderId: {OrderId}, CorrelationId: {CorrelationId}",
                    orderId, correlationId);
                throw;
            }
        }
    }

    /// <summary>
    /// Validate order request
    /// Throws ArgumentException if invalid
    /// </summary>
    private static void ValidateRequest(OrderRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request), "Order request cannot be null");

        if (request.CustomerId == Guid.Empty)
            throw new ArgumentException("CustomerId cannot be empty", nameof(request.CustomerId));

        if (request.Items == null || request.Items.Length == 0)
            throw new ArgumentException("Order must have at least one item", nameof(request.Items));

        foreach (var item in request.Items)
        {
            if (item.ProductId == Guid.Empty)
                throw new ArgumentException("ProductId cannot be empty", nameof(item.ProductId));

            if (item.Quantity <= 0)
                throw new ArgumentException($"Quantity must be positive, got {item.Quantity}", nameof(item.Quantity));

            if (item.UnitPrice < 0)
                throw new ArgumentException($"UnitPrice cannot be negative, got {item.UnitPrice}", nameof(item.UnitPrice));
        }
    }
}

/// <summary>
/// Result of place order operation
/// </summary>
public record PlaceOrderResult
{
    /// <summary>
    /// Order ID (new)
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Customer ID
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Order status (Pending)
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Total amount
    /// </summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>
    /// When order was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Number of items in order
    /// </summary>
    public required int ItemCount { get; init; }
}
