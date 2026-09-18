using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notification.Service.Data;
using NotificationService = Notification.Service.Entities;
using Contracts.Events;

namespace Notification.Service.Handlers;

/// <summary>
/// Handler for creating notifications from events
/// 
/// Event → Notification Mapping:
/// - OrderPlaced → "Welcome to our store, your order has been placed"
/// - InventoryRejected → "Item out of stock, order cancelled"
/// - PaymentFailed → "Payment failed, please try again"
/// - OrderConfirmed → "Your order has been confirmed!"
/// - OrderFailed → "Your order was cancelled"
/// </summary>
public interface ICreateNotificationHandler
{
    Task<CreateNotificationResult> HandleAsync(
        OrderPlacedEvent @event,
        Guid correlationId);

    Task<CreateNotificationResult> HandleAsync(
        OrderConfirmedEvent @event,
        Guid correlationId);

    Task<CreateNotificationResult> HandleAsync(
        OrderFailedEvent @event,
        Guid correlationId);

    Task<CreateNotificationResult> HandleAsync(
        PaymentFailedEvent @event,
        Guid correlationId);

    Task<CreateNotificationResult> HandleAsync(
        InventoryRejectedEvent @event,
        Guid correlationId);
}

/// <summary>
/// Result of notification creation
/// </summary>
public record CreateNotificationResult
{
    public required bool Success { get; init; }
    public required Guid NotificationId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid MessageId { get; init; }
    public bool IsIdempotent { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Implementation
/// </summary>
public class CreateNotificationHandler : ICreateNotificationHandler
{
    private readonly NotificationDbContext _dbContext;
    private readonly ILogger<CreateNotificationHandler> _logger;

    public CreateNotificationHandler(
        NotificationDbContext dbContext,
        ILogger<CreateNotificationHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CreateNotificationResult> HandleAsync(
        OrderPlacedEvent @event,
        Guid correlationId)
    {
        return await CreateNotificationAsync(
            @event.OrderId,
            @event.CustomerId,
            @event.MessageId,
            correlationId,
            "OrderPlaced",
            subject: "Order Placed",
            message: $"Your order #{@event.OrderId:N} has been placed with {string.Join(", ", @event.Items.Select(i => i.Quantity + "x #" + i.ProductId.ToString("N")))}"
        );
    }

    public async Task<CreateNotificationResult> HandleAsync(
        OrderConfirmedEvent @event,
        Guid correlationId)
    {
        return await CreateNotificationAsync(
            @event.OrderId,
            @event.CustomerId,
            @event.MessageId,
            correlationId,
            "OrderConfirmed",
            subject: "Order Confirmed",
            message: $"Your order #{@event.OrderId:N} has been confirmed and is being prepared for shipment."
        );
    }

    public async Task<CreateNotificationResult> HandleAsync(
        OrderFailedEvent @event,
        Guid correlationId)
    {
        return await CreateNotificationAsync(
            @event.OrderId,
            @event.CustomerId,
            @event.MessageId,
            correlationId,
            "OrderFailed",
            subject: "Order Cancelled",
            message: $"Your order #{@event.OrderId:N} has been cancelled. Reason: {@event.Reason}"
        );
    }

    public async Task<CreateNotificationResult> HandleAsync(
        PaymentFailedEvent @event,
        Guid correlationId)
    {
        return await CreateNotificationAsync(
            @event.OrderId,
            @event.CustomerId,
            @event.MessageId,
            correlationId,
            "PaymentFailed",
            subject: "Payment Failed",
            message: $"Payment for order #{@event.OrderId:N} failed: {@event.Reason}. Please try again."
        );
    }

    public async Task<CreateNotificationResult> HandleAsync(
        InventoryRejectedEvent @event,
        Guid correlationId)
    {
        return await CreateNotificationAsync(
            @event.OrderId,
            Guid.Empty,  // No customer ID in this event, will use order lookup (stub for now)
            @event.MessageId,
            correlationId,
            "InventoryRejected",
            subject: "Item Out of Stock",
            message: $"Order #{@event.OrderId:N} cancelled: {@event.Reason}"
        );
    }

    // ========================================================================
    // Private Implementation
    // ========================================================================

    private async Task<CreateNotificationResult> CreateNotificationAsync(
        Guid orderId,
        Guid customerId,
        Guid messageId,
        Guid correlationId,
        string eventType,
        string subject,
        string message)
    {
        // Check idempotency
        var existing = await _dbContext.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.MessageId == messageId);

        if (existing != null)
        {
            _logger.LogInformation(
                "Notification already created (idempotent). MessageId: {MessageId}, " +
                "OrderId: {OrderId}, EventType: {EventType}",
                messageId, orderId, eventType);

            return new CreateNotificationResult
            {
                Success = true,
                NotificationId = existing.NotificationId,
                OrderId = orderId,
                MessageId = messageId,
                IsIdempotent = true
            };
        }

        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var notification = new NotificationService.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    OrderId = orderId,
                    CustomerId = customerId,
                    EventType = eventType,
                    Subject = subject,
                    Message = message,
                    Status = NotificationService.NotificationStatus.Pending,
                    MessageId = messageId,
                    CorrelationId = correlationId,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Notifications.Add(notification);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Notification created. NotificationId: {NotificationId}, " +
                    "OrderId: {OrderId}, EventType: {EventType}, MessageId: {MessageId}",
                    notification.NotificationId, orderId, eventType, messageId);

                return new CreateNotificationResult
                {
                    Success = true,
                    NotificationId = notification.NotificationId,
                    OrderId = orderId,
                    MessageId = messageId,
                    IsIdempotent = false
                };
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error creating notification. MessageId: {MessageId}, OrderId: {OrderId}",
                    messageId, orderId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error creating notification. MessageId: {MessageId}, OrderId: {OrderId}",
                    messageId, orderId);
                throw;
            }
        }
    }
}
