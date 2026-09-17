using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orders.Service.Data;
using Orders.Service.DTOs;
using Orders.Service.Entities;

namespace Orders.Service.Handlers;

/// <summary>
/// Application handler for querying order status
/// 
/// Responsibilities:
/// 1. Look up order by ID
/// 2. Include related items and status history
/// 3. Return complete order state
/// 4. Return null if not found (endpoint handles 404)
/// </summary>
public class GetOrderStatusHandler
{
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<GetOrderStatusHandler> _logger;

    public GetOrderStatusHandler(OrderDbContext dbContext, ILogger<GetOrderStatusHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get order status by order ID
    /// 
    /// Process:
    /// 1. Query order with related entities
    /// 2. Load items (line items)
    /// 3. Load status transitions (audit trail)
    /// 4. Return order response or null
    /// </summary>
    /// <param name="orderId">Order ID to query</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <returns>Order response or null if not found</returns>
    public async Task<OrderResponse?> HandleAsync(
        Guid orderId,
        Guid correlationId)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId cannot be empty", nameof(orderId));

        try
        {
            // Query order with related entities
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .Include(o => o.StatusTransitions)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                _logger.LogInformation(
                    "Order not found. OrderId: {OrderId}, CorrelationId: {CorrelationId}",
                    orderId, correlationId);
                return null;
            }

            var response = OrderResponse.FromEntity(order);

            _logger.LogInformation(
                "Order retrieved. OrderId: {OrderId}, Status: {Status}, Items: {ItemCount}, CorrelationId: {CorrelationId}",
                orderId, order.Status, order.Items.Count, correlationId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error retrieving order. OrderId: {OrderId}, CorrelationId: {CorrelationId}",
                orderId, correlationId);
            throw;
        }
    }
}
