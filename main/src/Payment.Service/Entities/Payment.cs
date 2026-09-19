namespace Payment.Service.Entities;

/// <summary>
/// Payment entity representing a charge attempt for an order
/// 
/// Design: Immutable snapshot of payment state
/// - Status tracks payment lifecycle: Pending → Charged/Failed
/// - Version enables optimistic concurrency
/// - MessageId prevents duplicate charges (idempotency)
/// - CorrelationId enables end-to-end tracing
/// - TransactionId from payment processor (for audit)
/// </summary>
public class Payment
{
    /// <summary>
    /// Unique payment identifier (PK)
    /// </summary>
    public Guid PaymentId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Order this payment is for
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Customer making the payment
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Amount to charge
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Current payment status
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Transaction ID from payment processor (for audit/lookup)
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// Unique message ID from Kafka (prevents duplicate charges)
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Correlation ID from originating order (tracing)
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// When payment was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When payment was processed (charged or failed)
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token
    /// Incremented on each update, prevents lost updates
    /// </summary>
    public byte[] Version { get; set; } = new byte[] { 0 };

    /// <summary>
    /// Navigation to refunds for this payment
    /// </summary>
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

/// <summary>
/// Payment lifecycle status
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Payment not yet processed
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Payment charged successfully
    /// </summary>
    Charged = 1,

    /// <summary>
    /// Payment failed
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Payment partially or fully refunded
    /// </summary>
    Refunded = 3
}

