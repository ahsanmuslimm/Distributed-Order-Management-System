namespace Saga.Orchestrator.Entities;

/// <summary>
/// Saga State - Orchestration state machine for order sagas
/// 
/// State Machine Transitions:
/// 
/// START
///   ↓
/// PENDING (Order created, waiting for inventory)
///   ↓ [InventoryReserved]
/// INVENTORY_RESERVED (Inventory ok, waiting for payment)
///   ↓ [PaymentCharged]
/// PAYMENT_CHARGED (Payment ok, waiting for confirmation)
///   ↓ [OrderConfirmed]
/// COMPLETED ✓
/// 
/// COMPENSATION PATHS:
/// 
/// PENDING → [InventoryRejected]
///   → Send FailOrder
///   → FAILED
/// 
/// INVENTORY_RESERVED → [PaymentFailed]
///   → Send ReleaseInventory (compensation)
///   → COMPENSATION_IN_PROGRESS
///   → FAILED
/// 
/// PAYMENT_CHARGED → [OrderConfirmationFailed]
///   → Send RefundPayment (compensation)
///   → Send ReleaseInventory (compensation)
///   → COMPENSATION_IN_PROGRESS
///   → FAILED
/// 
/// KEY INSIGHT:
/// Each state represents a committed transaction that may need compensation.
/// If later step fails, saga automatically reverses earlier steps.
/// </summary>
public class SagaState
{
    public Guid SagaId { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal OrderAmount { get; init; }
    
    public SagaStatus Status { get; set; }  // Current state in machine
    public string? CurrentStep { get; set; } // What we're waiting for
    
    // Timeline
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    
    // Compensation tracking
    public bool InventoryCompensationSent { get; set; }
    public bool PaymentCompensationSent { get; set; }
    public int CompensationRetryCount { get; set; }
    
    // Error tracking
    public string? FailureReason { get; set; }
    public string? LastError { get; set; }
    
    // Tracing
    public Guid CorrelationId { get; init; }
    
    // Timeout handling
    public DateTime? TimeoutAt { get; set; }  // When to consider saga stuck
    public int TimeoutRetries { get; set; }  // How many times we've retried
    
    // Version for optimistic concurrency
    public int Version { get; set; } = 0;
}

/// <summary>
/// Saga Status - State machine states
/// 
/// PENDING: Order placed, no transactions yet
/// INVENTORY_RESERVED: Inventory confirmed, awaiting payment
/// PAYMENT_CHARGED: Payment confirmed, awaiting order confirmation
/// COMPLETED: All steps succeeded
/// COMPENSATION_IN_PROGRESS: A step failed, compensating earlier steps
/// FAILED: Saga failed (compensation complete or skipped)
/// </summary>
public enum SagaStatus
{
    Pending = 0,                    // Order received
    InventoryReserved = 1,          // Inventory ok
    PaymentCharged = 2,             // Payment ok
    Completed = 3,                  // ✓ Order confirmed
    
    CompensationInProgress = 10,    // Compensating failed steps
    Failed = 11,                    // ✗ Saga failed
    
    TimedOut = 20                   // Stuck for too long
}

/// <summary>
/// Saga Event Log - Immutable record of saga progress
/// </summary>
public class SagaEventLog
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid SagaId { get; init; }
    public SagaEventType EventType { get; init; }
    public string Details { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Saga Event Types - What happened in the saga
/// </summary>
public enum SagaEventType
{
    // Happy path
    SagaStarted = 0,
    InventoryReserved = 1,
    PaymentCharged = 2,
    OrderConfirmed = 3,
    SagaCompleted = 4,
    
    // Failure path
    InventoryRejected = 10,
    PaymentFailed = 11,
    OrderConfirmationFailed = 12,
    
    // Compensation
    InventoryCompensationSent = 20,
    PaymentCompensationSent = 21,
    CompensationCompleted = 22,
    
    // Timeout
    SagaTimedOut = 30,
    TimeoutCompensationSent = 31
}
