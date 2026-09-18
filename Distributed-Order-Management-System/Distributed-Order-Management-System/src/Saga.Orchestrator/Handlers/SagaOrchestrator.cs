using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saga.Orchestrator.Data;
using Saga.Orchestrator.Domain;
using Saga.Orchestrator.Entities;
using Contracts.Events;
using Contracts.Commands;

namespace Saga.Orchestrator.Handlers;

/// <summary>
/// Saga Orchestrator
/// 
/// Orchestrates distributed sagas across Order, Inventory, and Payment services.
/// 
/// Responsibilities:
/// 1. Create saga state machine when order placed
/// 2. Process events from services (InventoryReserved, PaymentCharged, etc.)
/// 3. On success: Move to next step (send next command)
/// 4. On failure: Trigger compensation (auto-reverse previous steps)
/// 5. Handle timeouts (stuck sagas)
/// 
/// Design:
/// - Idempotent event processing (CorrelationId deduplication)
/// - Compensation is automatic (no manual intervention)
/// - Full audit trail (SagaEventLog)
/// - Optimistic concurrency (Version field)
/// </summary>
public interface ISagaOrchestrator
{
    Task<OrchestrateResult> HandleOrderPlacedAsync(
        OrderPlacedEvent @event,
        Guid correlationId);

    Task<OrchestrateResult> HandleInventoryReservedAsync(
        InventoryReservedEvent @event,
        Guid correlationId);

    Task<OrchestrateResult> HandleInventoryRejectedAsync(
        InventoryRejectedEvent @event,
        Guid correlationId);

    Task<OrchestrateResult> HandlePaymentChargedAsync(
        PaymentChargedEvent @event,
        Guid correlationId);

    Task<OrchestrateResult> HandlePaymentFailedAsync(
        PaymentFailedEvent @event,
        Guid correlationId);

    Task<OrchestrateResult> HandleOrderConfirmedAsync(
        OrderConfirmedEvent @event,
        Guid correlationId);
}

public record OrchestrateResult
{
    public required bool Success { get; init; }
    public required Guid SagaId { get; init; }
    public required SagaStatus NewStatus { get; init; }
    public List<string> CommandsIssuedToServices { get; init; } = new();
    public string? ErrorMessage { get; init; }
}

public class SagaOrchestratorHandler : ISagaOrchestrator
{
    private readonly SagaDbContext _dbContext;
    private readonly ISagaStateMachine _stateMachine;
    private readonly ILogger<SagaOrchestratorHandler> _logger;

    public SagaOrchestratorHandler(
        SagaDbContext dbContext,
        ISagaStateMachine stateMachine,
        ILogger<SagaOrchestratorHandler> logger)
    {
        _dbContext = dbContext;
        _stateMachine = stateMachine;
        _logger = logger;
    }

    public async Task<OrchestrateResult> HandleOrderPlacedAsync(
        OrderPlacedEvent @event,
        Guid correlationId)
    {
        // Create new saga
        var sagaId = Guid.NewGuid();
        var saga = new SagaState
        {
            SagaId = sagaId,
            OrderId = @event.OrderId,
            CustomerId = @event.CustomerId,
            OrderAmount = CalculateOrderAmount(@event.Items),
            Status = SagaStatus.Pending,
            CurrentStep = "Reserving inventory...",
            CorrelationId = correlationId
        };

        _dbContext.SagaStates.Add(saga);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Saga started. SagaId: {SagaId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}",
            sagaId, @event.OrderId, correlationId);

        // Next step: Reserve inventory
        return new OrchestrateResult
        {
            Success = true,
            SagaId = sagaId,
            NewStatus = SagaStatus.Pending,
            CommandsIssuedToServices = new() { "ReserveInventory" }
        };
    }

    public async Task<OrchestrateResult> HandleInventoryReservedAsync(
        InventoryReservedEvent @event,
        Guid correlationId)
    {
        // Find saga by order ID
        var saga = await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId);

        if (saga == null)
            return FailedResult($"Saga not found for OrderId: {@event.OrderId}");

        // Process event through state machine
        var @evt = new SagaEvent
        {
            SagaId = saga.SagaId,
            OrderId = @event.OrderId,
            EventType = SagaEventType.InventoryReserved,
            CorrelationId = correlationId
        };

        var transition = _stateMachine.ProcessEvent(saga, @evt);

        if (!transition.IsValid)
            return FailedResult(transition.ErrorMessage ?? "Invalid transition");

        // Update saga state
        saga.Status = transition.NewStatus;
        saga.CurrentStep = "Charging payment...";

        // Log event
        var eventLog = new SagaEventLog
        {
            SagaId = saga.SagaId,
            EventType = SagaEventType.InventoryReserved,
            Details = $"Inventory reserved for product {string.Join(", ", @event.ProductId)}"
        };
        _dbContext.SagaEventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Saga progressed: inventory reserved. SagaId: {SagaId}, OrderId: {OrderId}",
            saga.SagaId, saga.OrderId);

        return new OrchestrateResult
        {
            Success = true,
            SagaId = saga.SagaId,
            NewStatus = transition.NewStatus,
            CommandsIssuedToServices = new() { "ChargePayment" }
        };
    }

    public async Task<OrchestrateResult> HandleInventoryRejectedAsync(
        InventoryRejectedEvent @event,
        Guid correlationId)
    {
        // Inventory rejected = saga fails immediately (no compensation needed)
        var saga = await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId);

        if (saga == null)
            return FailedResult($"Saga not found for OrderId: {@event.OrderId}");

        saga.Status = SagaStatus.Failed;
        saga.FailedAt = DateTime.UtcNow;
        saga.FailureReason = @event.Reason;

        var eventLog = new SagaEventLog
        {
            SagaId = saga.SagaId,
            EventType = SagaEventType.InventoryRejected,
            Details = @event.Reason,
            ErrorMessage = @event.Reason
        };
        _dbContext.SagaEventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync();

        _logger.LogWarning(
            "Saga failed: inventory rejected. SagaId: {SagaId}, OrderId: {OrderId}, Reason: {Reason}",
            saga.SagaId, saga.OrderId, @event.Reason);

        return new OrchestrateResult
        {
            Success = true,
            SagaId = saga.SagaId,
            NewStatus = SagaStatus.Failed,
            CommandsIssuedToServices = new() { "FailOrder" },
            ErrorMessage = @event.Reason
        };
    }

    public async Task<OrchestrateResult> HandlePaymentChargedAsync(
        PaymentChargedEvent @event,
        Guid correlationId)
    {
        var saga = await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId);

        if (saga == null)
            return FailedResult($"Saga not found for OrderId: {@event.OrderId}");

        var @evt = new SagaEvent
        {
            SagaId = saga.SagaId,
            OrderId = @event.OrderId,
            EventType = SagaEventType.PaymentCharged,
            CorrelationId = correlationId
        };

        var transition = _stateMachine.ProcessEvent(saga, @evt);

        if (!transition.IsValid)
            return FailedResult(transition.ErrorMessage ?? "Invalid transition");

        saga.Status = transition.NewStatus;
        saga.CurrentStep = "Confirming order...";

        var eventLog = new SagaEventLog
        {
            SagaId = saga.SagaId,
            EventType = SagaEventType.PaymentCharged,
            Details = $"Payment charged: {string.Format("{0:C}", @event.Amount)}"
        };
        _dbContext.SagaEventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Saga progressed: payment charged. SagaId: {SagaId}, OrderId: {OrderId}, Amount: {Amount}",
            saga.SagaId, saga.OrderId, @event.Amount);

        return new OrchestrateResult
        {
            Success = true,
            SagaId = saga.SagaId,
            NewStatus = transition.NewStatus,
            CommandsIssuedToServices = new() { "ConfirmOrder" }
        };
    }

    public async Task<OrchestrateResult> HandlePaymentFailedAsync(
        PaymentFailedEvent @event,
        Guid correlationId)
    {
        // COMPENSATION TRIGGERED
        var saga = await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId);

        if (saga == null)
            return FailedResult($"Saga not found for OrderId: {@event.OrderId}");

        var @evt = new SagaEvent
        {
            SagaId = saga.SagaId,
            OrderId = @event.OrderId,
            EventType = SagaEventType.PaymentFailed,
            Reason = @event.Reason,
            CorrelationId = correlationId
        };

        var transition = _stateMachine.ProcessEvent(saga, @evt);

        if (!transition.IsValid)
            return FailedResult(transition.ErrorMessage ?? "Invalid transition");

        saga.Status = transition.NewStatus;
        saga.FailedAt = DateTime.UtcNow;
        saga.FailureReason = @event.Reason;
        saga.InventoryCompensationSent = true;

        var eventLog = new SagaEventLog
        {
            SagaId = saga.SagaId,
            EventType = SagaEventType.PaymentFailed,
            Details = @event.Reason,
            ErrorMessage = @event.Reason
        };
        _dbContext.SagaEventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync();

        _logger.LogWarning(
            "Saga failed: payment failed. SagaId: {SagaId}, OrderId: {OrderId}, Reason: {Reason}. " +
            "Sending compensation: {Compensations}",
            saga.SagaId, saga.OrderId, @event.Reason, string.Join(", ", transition.CommandsToSend));

        return new OrchestrateResult
        {
            Success = true,
            SagaId = saga.SagaId,
            NewStatus = transition.NewStatus,
            CommandsIssuedToServices = transition.CommandsToSend,
            ErrorMessage = @event.Reason
        };
    }

    public async Task<OrchestrateResult> HandleOrderConfirmedAsync(
        OrderConfirmedEvent @event,
        Guid correlationId)
    {
        var saga = await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.OrderId == @event.OrderId);

        if (saga == null)
            return FailedResult($"Saga not found for OrderId: {@event.OrderId}");

        var @evt = new SagaEvent
        {
            SagaId = saga.SagaId,
            OrderId = @event.OrderId,
            EventType = SagaEventType.OrderConfirmed,
            CorrelationId = correlationId
        };

        var transition = _stateMachine.ProcessEvent(saga, @evt);

        if (!transition.IsValid)
            return FailedResult(transition.ErrorMessage ?? "Invalid transition");

        saga.Status = transition.NewStatus;
        saga.CompletedAt = DateTime.UtcNow;

        var eventLog = new SagaEventLog
        {
            SagaId = saga.SagaId,
            EventType = SagaEventType.OrderConfirmed,
            Details = "Order confirmed and saga completed successfully"
        };
        _dbContext.SagaEventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Saga completed successfully. SagaId: {SagaId}, OrderId: {OrderId}",
            saga.SagaId, saga.OrderId);

        return new OrchestrateResult
        {
            Success = true,
            SagaId = saga.SagaId,
            NewStatus = transition.NewStatus,
            CommandsIssuedToServices = new()
        };
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    private OrchestrateResult FailedResult(string? errorMessage)
    {
        return new OrchestrateResult
        {
            Success = false,
            SagaId = Guid.Empty,
            NewStatus = SagaStatus.Failed,
            ErrorMessage = errorMessage
        };
    }

    private decimal CalculateOrderAmount(OrderItem[] items)
    {
        return items.Sum(i => i.UnitPrice * i.Quantity);
    }
}
