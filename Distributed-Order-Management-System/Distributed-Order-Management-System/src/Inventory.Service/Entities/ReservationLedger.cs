namespace Inventory.Service.Entities;

/// <summary>
/// Immutable ledger of inventory movements (reserves and releases)
/// 
/// Design: Append-only ledger (no updates/deletes)
/// - Every reserve operation creates a new ledger entry
/// - Every release operation creates a new ledger entry
/// - Current stock = InitialStock - sum(reserves) + sum(releases)
/// 
/// Why this design?
/// 1. Audit trail: Complete history of all inventory movements
/// 2. Deterministic: Stock calculation is reproducible
/// 3. Idempotent: Duplicate MessageIds detected via unique constraint
/// 4. Reversible: Release entries can "undo" reserve entries
/// </summary>
public class ReservationLedger
{
    /// <summary>
    /// Unique ledger entry identifier (PK)
    /// </summary>
    public Guid LedgerId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Product being reserved/released
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Order this ledger entry is for
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Quantity being reserved or released
    /// Always positive (sign is determined by Type)
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Type of movement: Reserve or Release
    /// </summary>
    public LedgerType Type { get; set; }

    /// <summary>
    /// Unique message ID from Kafka
    /// Used for idempotency: duplicate MessageIds are rejected
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Status of this ledger entry
    /// Pending = awaiting processing
    /// Processed = successfully applied
    /// Rejected = validation failed
    /// </summary>
    public LedgerStatus Status { get; set; } = LedgerStatus.Pending;

    /// <summary>
    /// When this entry was created (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this entry was processed (UTC)
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Correlation ID from the originating command
    /// For distributed tracing
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Navigation property to product
    /// </summary>
    public virtual Product? Product { get; set; }
}

/// <summary>
/// Type of inventory movement
/// </summary>
public enum LedgerType
{
    /// <summary>
    /// Reserve inventory (order placement)
    /// </summary>
    Reserve = 0,

    /// <summary>
    /// Release inventory (order cancellation/compensation)
    /// </summary>
    Release = 1
}

/// <summary>
/// Status of ledger entry processing
/// </summary>
public enum LedgerStatus
{
    /// <summary>
    /// Entry received, awaiting processing
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Entry successfully processed
    /// </summary>
    Processed = 1,

    /// <summary>
    /// Entry rejected (e.g., insufficient stock)
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Entry moved to DLQ after max retries
    /// </summary>
    DeadLettered = 3
}
