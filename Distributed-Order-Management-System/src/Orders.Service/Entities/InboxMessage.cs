namespace Orders.Service.Entities;

/// <summary>
/// Idempotency store for message deduplication (Inbox Pattern)
/// 
/// Every Kafka message consumed by this service is recorded here with its MessageId.
/// Before processing a message, we check if it already exists in this table.
/// If it does, we skip processing (idempotent).
/// If not, we process it and then atomically insert into this table.
/// 
/// This prevents duplicate effects from at-least-once message delivery.
/// </summary>
public class InboxMessage
{
    /// <summary>
    /// The message ID from Kafka header (PK)
    /// Must be globally unique per message producer
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// The order this message affects (FK)
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Type of message (e.g., "ConfirmOrderCommand", "FailOrderCommand")
    /// </summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// Full message payload (JSON)
    /// Stored for debugging and replay scenarios
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// When the message was processed (UTC)
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing
    /// Links this message to the originating request
    /// </summary>
    public Guid CorrelationId { get; set; }
}
