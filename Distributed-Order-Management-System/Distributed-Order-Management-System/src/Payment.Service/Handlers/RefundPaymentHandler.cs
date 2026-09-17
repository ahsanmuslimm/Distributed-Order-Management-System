using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Service.Data;
using Payment.Service.Entities;

namespace Payment.Service.Handlers;

/// <summary>
/// Handler for RefundPayment command (Compensation)
/// 
/// Called when saga needs to reverse a charge:
/// 1. Order cancelled → Refund payment
/// 2. Payment processing failed → Refund payment (cleanup)
/// 3. Customer requests refund → Refund payment
/// 
/// Idempotent: Duplicate MessageIds are safely ignored
/// 
/// Used in Compensation Flow:
/// 1. Payment charged successfully
/// 2. But later step fails (e.g., inventory insufficient)
/// 3. SagaOrchestrator sends RefundPayment command
/// 4. RefundHandler creates Refund entry
/// 5. Publishes PaymentRefundedEvent → InventoryRelease triggered
/// 6. Stock automatically restored + payment refunded (all automatic!)
/// </summary>
public class RefundPaymentHandler
{
    private readonly PaymentDbContext _dbContext;
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(
        PaymentDbContext dbContext,
        ILogger<RefundPaymentHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Handle refund payment command
    /// </summary>
    public async Task<RefundPaymentResult> HandleAsync(
        RefundPaymentCommand command,
        Guid correlationId)
    {
        ValidateCommand(command);

        // Check if already processed (Inbox Pattern)
        var alreadyProcessed = await _dbContext.Refunds
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == command.MessageId);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Refund already processed (idempotent). PaymentId: {PaymentId}, " +
                "Amount: {Amount}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                command.PaymentId, command.Amount, command.MessageId, correlationId);

            return new RefundPaymentResult
            {
                Success = true,
                PaymentId = command.PaymentId,
                Amount = command.Amount,
                Reason = "Already refunded (idempotent)",
                IsIdempotent = true
            };
        }

        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // Verify payment exists and is in Charged state
                var payment = await _dbContext.Payments
                    .FirstOrDefaultAsync(p => p.PaymentId == command.PaymentId);

                if (payment == null)
                {
                    _logger.LogWarning(
                        "Payment not found for refund. PaymentId: {PaymentId}, " +
                        "MessageId: {MessageId}",
                        command.PaymentId, command.MessageId);

                    // Still create refund record for audit trail
                    var refundEntry = new Refund
                    {
                        RefundId = Guid.NewGuid(),
                        PaymentId = command.PaymentId,
                        Amount = command.Amount,
                        Status = RefundStatus.Failed,
                        MessageId = command.MessageId,
                        CorrelationId = correlationId,
                        ProcessedAt = DateTime.UtcNow
                    };

                    _dbContext.Refunds.Add(refundEntry);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return new RefundPaymentResult
                    {
                        Success = false,
                        PaymentId = command.PaymentId,
                        Amount = command.Amount,
                        Reason = "Payment not found",
                        IsIdempotent = false
                    };
                }

                if (payment.Status != PaymentStatus.Charged)
                {
                    _logger.LogWarning(
                        "Cannot refund payment with status: {Status}. PaymentId: {PaymentId}, " +
                        "MessageId: {MessageId}",
                        payment.Status, command.PaymentId, command.MessageId);

                    // Still create refund record for audit trail
                    var refundEntry = new Refund
                    {
                        RefundId = Guid.NewGuid(),
                        PaymentId = command.PaymentId,
                        Amount = command.Amount,
                        Status = RefundStatus.Failed,
                        MessageId = command.MessageId,
                        CorrelationId = correlationId,
                        ProcessedAt = DateTime.UtcNow
                    };

                    _dbContext.Refunds.Add(refundEntry);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return new RefundPaymentResult
                    {
                        Success = false,
                        PaymentId = command.PaymentId,
                        Amount = command.Amount,
                        Reason = $"Payment status is {payment.Status}, cannot refund",
                        IsIdempotent = false
                    };
                }

                // Create refund entry
                var successRefund = new Refund
                {
                    RefundId = Guid.NewGuid(),
                    PaymentId = command.PaymentId,
                    Amount = command.Amount,
                    Status = RefundStatus.Processed,
                    MessageId = command.MessageId,
                    CorrelationId = correlationId,
                    ProcessedAt = DateTime.UtcNow
                };

                _dbContext.Refunds.Add(successRefund);

                // Update payment status to Refunded
                payment.Status = PaymentStatus.Refunded;

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Payment refunded (compensation). PaymentId: {PaymentId}, Amount: {Amount}, " +
                    "MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                    command.PaymentId, command.Amount, command.MessageId, correlationId);

                return new RefundPaymentResult
                {
                    Success = true,
                    PaymentId = command.PaymentId,
                    Amount = command.Amount,
                    Reason = "Payment refunded successfully",
                    IsIdempotent = false
                };
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error during refund. PaymentId: {PaymentId}, MessageId: {MessageId}",
                    command.PaymentId, command.MessageId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error during refund. PaymentId: {PaymentId}, MessageId: {MessageId}",
                    command.PaymentId, command.MessageId);
                throw;
            }
        }
    }

    private void ValidateCommand(RefundPaymentCommand command)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (command.PaymentId == Guid.Empty)
            throw new ArgumentException("PaymentId cannot be empty", nameof(command.PaymentId));

        if (command.Amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(command.Amount));

        if (command.MessageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty", nameof(command.MessageId));
    }
}

/// <summary>
/// Command for refunding payment (compensation)
/// </summary>
public record RefundPaymentCommand
{
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required Guid MessageId { get; init; }
}

/// <summary>
/// Result of refund operation
/// </summary>
public record RefundPaymentResult
{
    public required bool Success { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Reason { get; init; }
    public required bool IsIdempotent { get; init; }
}

