namespace Payment.Service.Entities;

/// <summary>
/// Refund entity representing a refund against a charged payment
/// 
/// Design: Immutable record of refund
/// - Links to Payment (charged payment being refunded)
/// - Tracks amount refunded (may be partial)
/// - Status: Pending → Processed
/// - MessageId prevents duplicate refunds (idempotency)
/// - CorrelationId for tracing
/// 
/// Used in Compensation Flow:
/// 1. Payment fails → PaymentFailedEvent
/// 2. SagaOrchestrator decides to compensate
/// 3. Sends RefundPayment command
/// 4. RefundHandler creates Refund entry
/// 5. Publishes PaymentRefundedEvent (triggers InventoryRelease)
/// </summary>
public class Refund
{
    /// <summary>
    /// Unique refund identifier (PK)
    /// </summary>
    public Guid RefundId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Payment being refunded (FK)
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// Amount to refund
    /// May be less than original payment (partial refund)
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Current refund status
    /// </summary>
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    /// <summary>
    /// Unique message ID from saga (prevents duplicate refunds)
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Correlation ID for tracing
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// When refund was requested
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When refund was processed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Navigation to payment being refunded
    /// </summary>
    public virtual Payment? Payment { get; set; }
}

/// <summary>
/// Refund processing status
/// </summary>
public enum RefundStatus
{
    /// <summary>
    /// Refund requested, awaiting processing
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Refund successfully processed
    /// </summary>
    Processed = 1,

    /// <summary>
    /// Refund failed
    /// </summary>
    Failed = 2
}

