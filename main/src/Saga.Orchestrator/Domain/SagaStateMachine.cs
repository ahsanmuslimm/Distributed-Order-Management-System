namespace Saga.Orchestrator.Domain;

using Saga.Orchestrator.Entities;
using SagaEventType = Saga.Orchestrator.Entities.SagaEventType;

/// <summary>
/// Saga State Machine
/// 
/// Encapsulates the state transitions for order sagas.
/// Validates transitions and determines which compensations to send.
/// 
/// Design:
/// - Immutable state machine (no side effects)
/// - Pure functions (input → output)
/// - Testable transitions
/// </summary>
public interface ISagaStateMachine
{
    /// <summary>
    /// Process event and return next state + commands to send
    /// </summary>
    SagaTransitionResult ProcessEvent(SagaState currentState, SagaEvent @event);

    /// <summary>
    /// Get compensation commands for given state
    /// </summary>
    IEnumerable<string> GetCompensationCommands(SagaState sagaState);

    /// <summary>
    /// Check if saga should timeout (stuck for too long)
    /// </summary>
    bool ShouldTimeout(SagaState sagaState, TimeSpan? customTimeout = null);
}

/// <summary>
/// Saga Event - Something happened in one of the services
/// </summary>
public record SagaEvent
{
    public required Guid SagaId { get; init; }
    public required Guid OrderId { get; init; }
    public required SagaEventType EventType { get; init; }
    public string? Reason { get; init; }
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Event Types
/// </summary>
public record SagaTransitionResult
{
    public required SagaStatus NewStatus { get; init; }
    public required bool IsValid { get; init; }  // Valid transition?
    public string? ErrorMessage { get; init; }
    public List<string> CommandsToSend { get; init; } = new();
}

/// <summary>
/// State Machine Implementation
/// </summary>
public class SagaStateMachine : ISagaStateMachine
{
    // Timeout configuration
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _compensationTimeout = TimeSpan.FromMinutes(10);

    public SagaTransitionResult ProcessEvent(SagaState currentState, SagaEvent @event)
    {
        // Validate saga ID matches
        if (currentState.SagaId != @event.SagaId)
            return InvalidTransition("Saga ID mismatch");

        // Process based on current state
        return currentState.Status switch
        {
            // ====================================================================
            // PENDING STATE: Waiting for inventory
            // ====================================================================
            SagaStatus.Pending => @event.EventType switch
            {
                SagaEventType.InventoryReserved =>
                    ValidTransition(SagaStatus.InventoryReserved, "Inventory reserved, charging payment..."),

                SagaEventType.InventoryRejected =>
                    Failure(SagaStatus.Failed, @event.Reason ?? "Inventory unavailable", 
                        commandsToSend: new[] { "FailOrder" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in Pending state")
            },

            // ====================================================================
            // INVENTORY_RESERVED STATE: Waiting for payment
            // ====================================================================
            SagaStatus.InventoryReserved => @event.EventType switch
            {
                SagaEventType.PaymentCharged =>
                    ValidTransition(SagaStatus.PaymentCharged, "Payment charged, confirming order..."),

                SagaEventType.PaymentFailed =>
                    // COMPENSATION: Release inventory
                    Failure(SagaStatus.CompensationInProgress, 
                        @event.Reason ?? "Payment failed",
                        commandsToSend: new[] { "ReleaseInventory" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in InventoryReserved state")
            },

            // ====================================================================
            // PAYMENT_CHARGED STATE: Waiting for confirmation
            // ====================================================================
            SagaStatus.PaymentCharged => @event.EventType switch
            {
                SagaEventType.OrderConfirmed =>
                    ValidTransition(SagaStatus.Completed, "Order confirmed!"),

                SagaEventType.OrderConfirmationFailed =>
                    // COMPENSATION: Refund payment AND release inventory
                    Failure(SagaStatus.CompensationInProgress,
                        @event.Reason ?? "Order confirmation failed",
                        commandsToSend: new[] { "RefundPayment", "ReleaseInventory" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in PaymentCharged state")
            },

            // ====================================================================
            // COMPENSATION_IN_PROGRESS STATE: Sending compensations
            // ====================================================================
            SagaStatus.CompensationInProgress => @event.EventType switch
            {
                SagaEventType.InventoryRejected =>
                    Failure(SagaStatus.Failed, "Compensation failed: inventory release rejected"),

                SagaEventType.PaymentFailed =>
                    Failure(SagaStatus.Failed, "Compensation failed: refund failed"),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} during compensation")
            },

            // ====================================================================
            // COMPLETED/FAILED: Terminal states
            // ====================================================================
            SagaStatus.Completed or SagaStatus.Failed =>
                InvalidTransition($"Cannot process events in terminal state {currentState.Status}"),

            _ => InvalidTransition($"Unknown state: {currentState.Status}")
        };
    }

    /// <summary>
    /// Get compensation commands for state
    /// </summary>
    public IEnumerable<string> GetCompensationCommands(SagaState sagaState)
    {
        return sagaState.Status switch
        {
            // If we reserved inventory but payment failed, release it
            SagaStatus.InventoryReserved => new[] { "ReleaseInventory" },

            // If we charged payment but order failed, refund + release
            SagaStatus.PaymentCharged => new[] { "RefundPayment", "ReleaseInventory" },

            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Check if saga should timeout
    /// </summary>
    public bool ShouldTimeout(SagaState sagaState, TimeSpan? customTimeout = null)
    {
        var timeout = customTimeout ?? _defaultTimeout;
        var elapsedTime = DateTime.UtcNow - sagaState.CreatedAt;

        return elapsedTime > timeout;
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    private SagaTransitionResult ValidTransition(SagaStatus newStatus, string nextStep)
    {
        return new SagaTransitionResult
        {
            NewStatus = newStatus,
            IsValid = true,
            ErrorMessage = null,
            CommandsToSend = new()
        };
    }

    private SagaTransitionResult Failure(
        SagaStatus newStatus,
        string reason,
        IEnumerable<string>? commandsToSend = null)
    {
        return new SagaTransitionResult
        {
            NewStatus = newStatus,
            IsValid = true,
            ErrorMessage = reason,
            CommandsToSend = commandsToSend?.ToList() ?? new()
        };
    }

    private SagaTransitionResult InvalidTransition(string reason)
    {
        return new SagaTransitionResult
        {
            NewStatus = SagaStatus.Failed,
            IsValid = false,
            ErrorMessage = reason,
            CommandsToSend = new()
        };
    }

    private string EventType(SagaEventType type)
    {
        return type.ToString();
    }
}
