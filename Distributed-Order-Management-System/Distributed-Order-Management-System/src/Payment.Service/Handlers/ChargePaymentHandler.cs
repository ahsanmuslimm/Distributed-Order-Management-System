using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Service.Data;
using Payment.Service.Domain;
using Payment.Service.Entities;

namespace Payment.Service.Handlers;

/// <summary>
/// Handler for ChargePayment command
/// 
/// Idempotent message handler that:
/// 1. Checks if message already processed (Inbox Pattern)
/// 2. Determines if payment should fail (failure injection)
/// 3. If success: Create Payment record (Charged) → Publish PaymentChargedEvent
/// 4. If failure: Create Payment record (Failed) → Publish PaymentFailedEvent
/// 5. Record in inbox (atomic with payment update)
/// 
/// Failure Injection:
/// - For testing saga compensation
/// - Configured probability of failure
/// - Allows chaos testing
/// </summary>
public class ChargePaymentHandler
{
    private readonly PaymentDbContext _dbContext;
    private readonly IPaymentFailureInjector _failureInjector;
    private readonly ILogger<ChargePaymentHandler> _logger;

    public ChargePaymentHandler(
        PaymentDbContext dbContext,
        IPaymentFailureInjector failureInjector,
        ILogger<ChargePaymentHandler> logger)
    {
        _dbContext = dbContext;
        _failureInjector = failureInjector;
        _logger = logger;
    }

    /// <summary>
    /// Handle charge payment command
    /// </summary>
    public async Task<ChargePaymentResult> HandleAsync(
        ChargePaymentCommand command,
        Guid correlationId)
    {
        ValidateCommand(command);

        // Check if already processed (Inbox Pattern)
        var alreadyProcessed = await _dbContext.Payments
            .AsNoTracking()
            .AnyAsync(p => p.MessageId == command.MessageId);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Charge already processed (idempotent). OrderId: {OrderId}, " +
                "Amount: {Amount}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                command.OrderId, command.Amount, command.MessageId, correlationId);

            return new ChargePaymentResult
            {
                Success = true,
                OrderId = command.OrderId,
                Amount = command.Amount,
                Reason = "Already charged (idempotent)",
                IsIdempotent = true
            };
        }

        // Determine if payment should fail (failure injection for testing)
        var shouldFail = _failureInjector.ShouldFail();

        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                if (shouldFail)
                {
                    // Create failed payment record
                    var failedPayment = new Payment
                    {
                        PaymentId = Guid.NewGuid(),
                        OrderId = command.OrderId,
                        CustomerId = command.CustomerId,
                        Amount = command.Amount,
                        Status = PaymentStatus.Failed,
                        MessageId = command.MessageId,
                        CorrelationId = correlationId,
                        ProcessedAt = DateTime.UtcNow
                    };

                    _dbContext.Payments.Add(failedPayment);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogWarning(
                        "Payment failed (injected). OrderId: {OrderId}, Amount: {Amount}, " +
                        "MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                        command.OrderId, command.Amount, command.MessageId, correlationId);

                    return new ChargePaymentResult
                    {
                        Success = false,
                        OrderId = command.OrderId,
                        Amount = command.Amount,
                        Reason = "Payment failed (simulated for testing)",
                        IsIdempotent = false
                    };
                }
                else
                {
                    // Create successful payment record
                    var transactionId = Guid.NewGuid().ToString("N")[..16];  // Simulate transaction ID

                    var successPayment = new Payment
                    {
                        PaymentId = Guid.NewGuid(),
                        OrderId = command.OrderId,
                        CustomerId = command.CustomerId,
                        Amount = command.Amount,
                        Status = PaymentStatus.Charged,
                        TransactionId = transactionId,
                        MessageId = command.MessageId,
                        CorrelationId = correlationId,
                        ProcessedAt = DateTime.UtcNow
                    };

                    _dbContext.Payments.Add(successPayment);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "Payment charged. OrderId: {OrderId}, Amount: {Amount}, " +
                        "TransactionId: {TransactionId}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                        command.OrderId, command.Amount, transactionId, command.MessageId, correlationId);

                    return new ChargePaymentResult
                    {
                        Success = true,
                        OrderId = command.OrderId,
                        Amount = command.Amount,
                        Reason = "Payment charged successfully",
                        IsIdempotent = false
                    };
                }
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error during charge. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error during charge. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
        }
    }

    private void ValidateCommand(ChargePaymentCommand command)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (command.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId cannot be empty", nameof(command.OrderId));

        if (command.CustomerId == Guid.Empty)
            throw new ArgumentException("CustomerId cannot be empty", nameof(command.CustomerId));

        if (command.Amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(command.Amount));

        if (command.MessageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty", nameof(command.MessageId));
    }
}

/// <summary>
/// Command for charging payment
/// </summary>
public record ChargePaymentCommand
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required Guid MessageId { get; init; }
}

/// <summary>
/// Result of charge operation
/// </summary>
public record ChargePaymentResult
{
    public required bool Success { get; init; }
    public required Guid OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Reason { get; init; }
    public required bool IsIdempotent { get; init; }
}

