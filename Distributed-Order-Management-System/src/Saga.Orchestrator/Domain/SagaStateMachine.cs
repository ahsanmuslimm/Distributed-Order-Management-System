using Saga.Orchestrator.Entities;

namespace Saga.Orchestrator.Domain;

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
    bool ShouldTimeout(SagaState sagaState, TimeSpan? timeout = null);
}

/// <summary>
/// Saga Event - Something happened in one of the services
/// </summary>
public record SagaEvent
{
    public required Guid SagaId { get; init; }
    public required Guid OrderId { get; init; }
    public required Entities.SagaEventType EventType { get; init; }
    public string? Reason { get; init; }
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Transition Result - What happens after processing event
/// </summary>
public record SagaTransitionResult
{
    public required Entities.SagaStatus NewStatus { get; init; }
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
            Entities.SagaStatus.Pending => @event.EventType switch
            {
                Entities.SagaEventType.InventoryReserved =>
                    ValidTransition(Entities.SagaStatus.InventoryReserved, "Inventory reserved, charging payment..."),

                Entities.SagaEventType.InventoryRejected =>
                    Failure(Entities.SagaStatus.Failed, @event.Reason ?? "Inventory unavailable", 
                        commandsToSend: new[] { "FailOrder" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in Pending state")
            },

            // ====================================================================
            // INVENTORY_RESERVED STATE: Waiting for payment
            // ====================================================================
            Entities.SagaStatus.InventoryReserved => @event.EventType switch
            {
                Entities.SagaEventType.PaymentCharged =>
                    ValidTransition(Entities.SagaStatus.PaymentCharged, "Payment charged, confirming order..."),

                Entities.SagaEventType.PaymentFailed =>
                    // COMPENSATION: Release inventory
                    Failure(Entities.SagaStatus.CompensationInProgress, 
                        @event.Reason ?? "Payment failed",
                        commandsToSend: new[] { "ReleaseInventory" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in InventoryReserved state")
            },

            // ====================================================================
            // PAYMENT_CHARGED STATE: Waiting for confirmation
            // ====================================================================
            Entities.SagaStatus.PaymentCharged => @event.EventType switch
            {
                Entities.SagaEventType.OrderConfirmed =>
                    ValidTransition(Entities.SagaStatus.Completed, "Order confirmed!"),

                Entities.SagaEventType.OrderConfirmationFailed =>
                    // COMPENSATION: Refund payment AND release inventory
                    Failure(Entities.SagaStatus.CompensationInProgress,
                        @event.Reason ?? "Order confirmation failed",
                        commandsToSend: new[] { "RefundPayment", "ReleaseInventory" }),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} in PaymentCharged state")
            },

            // ====================================================================
            // COMPENSATION_IN_PROGRESS STATE: Sending compensations
            // ====================================================================
            Entities.SagaStatus.CompensationInProgress => @event.EventType switch
            {
                Entities.SagaEventType.InventoryRejected =>
                    Failure(Entities.SagaStatus.Failed, "Compensation failed: inventory release rejected"),

                Entities.SagaEventType.PaymentFailed =>
                    Failure(Entities.SagaStatus.Failed, "Compensation failed: refund failed"),

                _ => InvalidTransition($"Unexpected event {EventType(@event.EventType)} during compensation")
            },

            // ====================================================================
            // COMPLETED/FAILED: Terminal states
            // ====================================================================
            Entities.SagaStatus.Completed or Entities.SagaStatus.Failed =>
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
            Entities.SagaStatus.InventoryReserved => new[] { "ReleaseInventory" },

            // If we charged payment but order failed, refund + release
            Entities.SagaStatus.PaymentCharged => new[] { "RefundPayment", "ReleaseInventory" },

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

    private SagaTransitionResult ValidTransition(Entities.SagaStatus newStatus, string nextStep)
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
        Entities.SagaStatus newStatus,
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
            NewStatus = Entities.SagaStatus.Failed,
            IsValid = false,
            ErrorMessage = reason,
            CommandsToSend = new()
        };
    }

    private string EventType(Entities.SagaEventType type)
    {
        return type.ToString();
    }
}
