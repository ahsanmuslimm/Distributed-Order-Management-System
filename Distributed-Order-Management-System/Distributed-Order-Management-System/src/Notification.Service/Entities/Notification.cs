namespace Notification.Service.Entities;

/// <summary>
/// Notification entity
/// 
/// Represents a notification to be sent to customer about order status changes.
/// Notifications are immutable once created; state changes tracked in status field.
/// 
/// Usage:
/// - OrderPlaced event → Create Welcome notification
/// - InventoryRejected event → Create OutOfStock notification
/// - PaymentFailed event → Create PaymentFailed notification
/// - OrderConfirmed event → Create OrderConfirmed notification
/// - OrderFailed event → Create OrderCancelled notification
/// </summary>
public class Notification
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public required string EventType { get; init; }  // OrderPlaced, PaymentFailed, etc.
    public required string Subject { get; init; }     // Email subject
    public required string Message { get; init; }     // Email body
    public NotificationStatus Status { get; set; }    // Pending, Sent, Failed, DLQ
    public int RetryCount { get; set; }               // Current retry count (0-3)
    public string? LastError { get; set; }            // Last error message
    public Guid MessageId { get; init; }              // Kafka message ID (idempotency)
    public Guid CorrelationId { get; init; }          // Trace ID
    public DateTime CreatedAt { get; init; }          // When notification was created
    public DateTime? LastAttemptAt { get; set; }      // When last delivery was attempted
    public DateTime? DeliveredAt { get; set; }        // When successfully delivered
}

/// <summary>
/// Notification Status
/// </summary>
public enum NotificationStatus
{
    Pending = 0,      // Waiting to be sent
    Sent = 1,         // Successfully delivered
    Failed = 2,       // Delivery failed permanently
    DLQ = 3           // In dead letter queue
}

/// <summary>
/// Retry ledger entry (immutable log of retry attempts)
/// </summary>
public class NotificationRetry
{
    public Guid RetryId { get; init; } = Guid.NewGuid();
    public Guid NotificationId { get; init; }  // FK to Notification
    public int AttemptNumber { get; init; }    // 1, 2, 3
    public string? ErrorMessage { get; init; } // Why it failed
    public DateTime AttemptedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan NextRetryIn { get; init; } // When to retry (calculated from policy)
}
