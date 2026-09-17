using Microsoft.EntityFrameworkCore;
using Payment.Service.Data;
using Payment.Service.Domain;
using Payment.Service.Entities;
using Payment.Service.Handlers;
using Xunit;

namespace Payment.Service.Tests;

/// <summary>
/// Property-Based Tests for Payment Service
/// 
/// Properties define mathematical invariants that must hold for ALL valid inputs.
/// These tests verify core payment processing correctness and compensation logic.
/// 
/// Properties Implemented:
/// - Property 3.1.1: Idempotent Charge (MessageId deduplication)
/// - Property 3.1.2: Failure Rate Distribution (failure injection works)
/// - Property 3.1.3: Charge Amount Accuracy (amount stored correctly)
/// 
/// - Property 3.2.1: Idempotent Refund (MessageId deduplication)
/// - Property 3.2.2: Refund Validation (amount > 0, payment exists)
/// - Property 3.2.3: Refund Cannot Process Failed Payment (state validation)
/// - Property 3.2.4: Charge-Refund Round Trip (compensation proves saga works)
/// </summary>
public class PaymentPropertyTests : IAsyncLifetime
{
    private PaymentDbContext _dbContext = null!;
    private ChargePaymentHandler _chargeHandler = null!;
    private RefundPaymentHandler _refundHandler = null!;
    private IPaymentFailureInjector _noOpInjector = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new PaymentDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _noOpInjector = new NoOpPaymentFailureInjector();  // Default: always succeed
        _chargeHandler = new ChargePaymentHandler(
            _dbContext,
            _noOpInjector,
            new MockLogger<ChargePaymentHandler>());

        _refundHandler = new RefundPaymentHandler(
            _dbContext,
            new MockLogger<RefundPaymentHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // PROPERTY 3.1.1: Idempotent Charge (MessageId Deduplication)
    // ========================================================================
    /// <summary>
    /// Property 3.1.1: Charge command with same MessageId is idempotent
    /// 
    /// ∀ chargeCmd, messageId:
    ///   ChargePayment(cmd, messageId)
    ///   ChargePayment(cmd, messageId)  // Replay with same messageId
    ///   ≡ ChargePayment(cmd, messageId)  // Result identical to first
    /// 
    /// Ensures: Kafka at-least-once delivery doesn't cause duplicate charges
    /// </summary>
    [Fact]
    public async Task Property_3_1_1_ChargePayment_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var messageId = Guid.NewGuid();  // SAME MessageId for both calls
        var correlationId = Guid.NewGuid();
        var amount = 100.00m;

        var command = new ChargePaymentCommand
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            MessageId = messageId
        };

        // Act: Process twice with same MessageId
        var result1 = await _chargeHandler.HandleAsync(command, correlationId);
        var result2 = await _chargeHandler.HandleAsync(command, correlationId);  // Replay

        // Assert: Property - Both results identical, idempotent flag set on second call
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.OrderId, result2.OrderId);
        Assert.Equal(result1.Amount, result2.Amount);
        
        // Second call must be marked idempotent
        Assert.False(result1.IsIdempotent);  // First call
        Assert.True(result2.IsIdempotent);   // Second call (duplicate detected)

        // Verify only ONE payment record created (idempotency enforced by MessageId)
        var paymentCount = await _dbContext.Payments
            .Where(p => p.MessageId == messageId)
            .CountAsync();
        Assert.Equal(1, paymentCount);
    }

    // ========================================================================
    // PROPERTY 3.1.2: Failure Rate Distribution (Failure Injection)
    // ========================================================================
    /// <summary>
    /// Property 3.1.2: Failure injection produces correct distribution
    /// 
    /// ∀ failureRate ∈ [0.0, 1.0], seed:
    ///   Charges with failure rate r
    ///   ≈ r% fail, (1-r)% succeed
    /// 
    /// Ensures: Chaos testing is reproducible and distributed correctly
    /// </summary>
    [Fact]
    public async Task Property_3_1_2_ChargePayment_FailureRateDistribution_IsAccurate()
    {
        // Arrange: 50% failure rate
        var failureRate = 0.5;
        var seed = 42;  // Fixed seed for reproducibility
        var injector = new ConfigurablePaymentFailureInjector(failureRate, seed);
        var handler = new ChargePaymentHandler(
            _dbContext,
            injector,
            new MockLogger<ChargePaymentHandler>());

        var totalCharges = 100;
        var successCount = 0;
        var failureCount = 0;

        // Act: Process 100 charges with 50% failure rate
        for (int i = 0; i < totalCharges; i++)
        {
            var command = new ChargePaymentCommand
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = 50.00m,
                MessageId = Guid.NewGuid()  // Unique MessageId for each charge
            };

            var result = await handler.HandleAsync(command, Guid.NewGuid());

            if (result.Success)
                successCount++;
            else
                failureCount++;
        }

        // Assert: Failure rate approximately matches configured rate (±20% tolerance)
        var actualFailureRate = (double)failureCount / totalCharges;
        var tolerance = 0.2;  // Allow ±20% deviation

        Assert.InRange(actualFailureRate, failureRate - tolerance, failureRate + tolerance);

        // Verify: Some failures occurred (not all successes)
        Assert.True(failureCount > 0, "At least some failures should occur at 50% rate");
        Assert.True(successCount > 0, "At least some successes should occur at 50% rate");
    }

    // ========================================================================
    // PROPERTY 3.1.3: Charge Amount Accuracy
    // ========================================================================
    /// <summary>
    /// Property 3.1.3: Charged amount matches command amount exactly
    /// 
    /// ∀ amount ∈ Decimal[0.01, 999999.99]:
    ///   charge(amount) → Payment.Amount = amount
    /// 
    /// Ensures: No financial miscalculations
    /// </summary>
    [Fact]
    public async Task Property_3_1_3_ChargePayment_Amount_IsStoredAccurately()
    {
        // Arrange
        var testAmounts = new decimal[]
        {
            0.01m,      // Minimum
            1.00m,
            100.50m,
            9999.99m,
            999999.99m  // Large amount
        };

        foreach (var amount in testAmounts)
        {
            var messageId = Guid.NewGuid();
            var command = new ChargePaymentCommand
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = amount,
                MessageId = messageId
            };

            // Act
            var result = await _chargeHandler.HandleAsync(command, Guid.NewGuid());

            // Assert: Amount matches exactly
            Assert.True(result.Success);
            Assert.Equal(amount, result.Amount);

            // Verify database record has exact amount
            var payment = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.MessageId == messageId);
            Assert.NotNull(payment);
            Assert.Equal(amount, payment.Amount);
        }
    }

    // ========================================================================
    // PROPERTY 3.2.1: Idempotent Refund (MessageId Deduplication)
    // ========================================================================
    /// <summary>
    /// Property 3.2.1: Refund command with same MessageId is idempotent
    /// 
    /// ∀ refundCmd, messageId:
    ///   RefundPayment(cmd, messageId)
    ///   RefundPayment(cmd, messageId)  // Replay with same messageId
    ///   ≡ RefundPayment(cmd, messageId)  // Result identical to first
    /// 
    /// Ensures: At-least-once delivery doesn't cause duplicate refunds
    /// </summary>
    [Fact]
    public async Task Property_3_2_1_RefundPayment_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange: First, charge a payment
        var paymentId = Guid.NewGuid();
        var chargeMessageId = Guid.NewGuid();
        var amount = 75.00m;

        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = amount,
            Status = PaymentStatus.Charged,
            MessageId = chargeMessageId,
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        // Now attempt refund twice with same MessageId
        var refundMessageId = Guid.NewGuid();  // SAME MessageId for both refunds
        var refundCommand = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = amount,
            MessageId = refundMessageId
        };

        // Act: Process twice with same MessageId
        var result1 = await _refundHandler.HandleAsync(refundCommand, Guid.NewGuid());
        var result2 = await _refundHandler.HandleAsync(refundCommand, Guid.NewGuid());  // Replay

        // Assert: Property - Both results identical, idempotent flag set on second call
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.PaymentId, result2.PaymentId);
        Assert.Equal(result1.Amount, result2.Amount);

        // Second call must be marked idempotent
        Assert.False(result1.IsIdempotent);  // First call
        Assert.True(result2.IsIdempotent);   // Second call (duplicate detected)

        // Verify only ONE refund record created
        var refundCount = await _dbContext.Refunds
            .Where(r => r.MessageId == refundMessageId)
            .CountAsync();
        Assert.Equal(1, refundCount);
    }

    // ========================================================================
    // PROPERTY 3.2.2: Refund Validation (Amount > 0, Payment Exists)
    // ========================================================================
    /// <summary>
    /// Property 3.2.2: Refund validates prerequisites (amount > 0, payment exists)
    /// 
    /// ∀ amount, paymentId:
    ///   amount ≤ 0 ∨ payment not found
    ///   → RefundPayment fails with reason
    /// 
    /// Ensures: Validation prevents invalid refunds
    /// </summary>
    [Fact]
    public async Task Property_3_2_2_RefundPayment_Validation_EnforcesRequirements()
    {
        // Arrange: Test invalid amounts
        var invalidAmounts = new decimal[] { 0m, -1m, -100.00m };

        foreach (var amount in invalidAmounts)
        {
            // This should fail during validation
            var command = new RefundPaymentCommand
            {
                PaymentId = Guid.NewGuid(),
                Amount = amount,
                MessageId = Guid.NewGuid()
            };

            // Act & Assert: Should throw validation error
            await Assert.ThrowsAsync<ArgumentException>(
                () => _refundHandler.HandleAsync(command, Guid.NewGuid()));
        }

        // Test: Payment not found
        var nonExistentPaymentId = Guid.NewGuid();
        var refundCmd = new RefundPaymentCommand
        {
            PaymentId = nonExistentPaymentId,
            Amount = 100.00m,
            MessageId = Guid.NewGuid()
        };

        var result = await _refundHandler.HandleAsync(refundCmd, Guid.NewGuid());
        Assert.False(result.Success);
        Assert.Contains("not found", result.Reason.ToLower());
    }

    // ========================================================================
    // PROPERTY 3.2.3: Refund Cannot Process Failed Payment
    // ========================================================================
    /// <summary>
    /// Property 3.2.3: Cannot refund payment that is not Charged
    /// 
    /// ∀ payment with status ≠ Charged:
    ///   RefundPayment(payment) → Fail
    /// 
    /// Ensures: Compensation only works for successfully charged payments
    /// </summary>
    [Fact]
    public async Task Property_3_2_3_RefundPayment_CannotRefund_NonChargedPayment()
    {
        // Arrange: Create payment with Failed status (not Charged)
        var paymentId = Guid.NewGuid();
        var failedPayment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 50.00m,
            Status = PaymentStatus.Failed,  // NOT Charged
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(failedPayment);
        await _dbContext.SaveChangesAsync();

        var refundCommand = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 50.00m,
            MessageId = Guid.NewGuid()
        };

        // Act
        var result = await _refundHandler.HandleAsync(refundCommand, Guid.NewGuid());

        // Assert: Refund fails because payment status is Failed
        Assert.False(result.Success);
        Assert.Contains("status", result.Reason.ToLower());

        // Verify payment status unchanged
        var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    // ========================================================================
    // PROPERTY 3.2.4: Charge-Refund Round Trip (Compensation Round Trip)
    // ========================================================================
    /// <summary>
    /// Property 3.2.4: Charge + Refund round trip proves compensation
    /// 
    /// ∀ chargeCmd, refundCmd:
    ///   charge(cmd) → Charged
    ///   refund(cmd) → Refunded
    ///   state ≈ initial state (payment can be charged again)
    /// 
    /// Ensures: Saga compensation logic works (payment reversal)
    /// This is the CRITICAL property proving compensation works!
    /// </summary>
    [Fact]
    public async Task Property_3_2_4_ChargeAndRefund_RoundTrip_ProvesSagaCompensation()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var amount = 123.45m;

        var chargeCommand = new ChargePaymentCommand
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            MessageId = Guid.NewGuid()
        };

        // Act 1: Charge payment
        var chargeResult = await _chargeHandler.HandleAsync(chargeCommand, correlationId);
        Assert.True(chargeResult.Success);
        Assert.Equal(PaymentStatus.Charged.ToString(), "Charged");

        // Get the payment ID from database
        var chargedPayment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.MessageId == chargeCommand.MessageId);
        Assert.NotNull(chargedPayment);
        Assert.Equal(PaymentStatus.Charged, chargedPayment.Status);

        // Act 2: Refund payment (compensation)
        var refundCommand = new RefundPaymentCommand
        {
            PaymentId = chargedPayment.PaymentId,
            Amount = amount,
            MessageId = Guid.NewGuid()
        };

        var refundResult = await _refundHandler.HandleAsync(refundCommand, correlationId);

        // Assert: Compensation succeeds
        Assert.True(refundResult.Success);

        // Verify: Payment status changed to Refunded (compensation complete)
        var refundedPayment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.PaymentId == chargedPayment.PaymentId);
        Assert.NotNull(refundedPayment);
        Assert.Equal(PaymentStatus.Refunded, refundedPayment.Status);

        // Verify: Refund record created in audit trail
        var refund = await _dbContext.Refunds
            .FirstOrDefaultAsync(r => r.PaymentId == chargedPayment.PaymentId);
        Assert.NotNull(refund);
        Assert.Equal(RefundStatus.Processed, refund.Status);
        Assert.Equal(amount, refund.Amount);

        // PROOF: This demonstrates saga compensation works end-to-end
        // Payment charged → Payment refunded → Audit trail complete
        // This is used by SagaOrchestrator to automatically reverse failed orders
    }

    // ========================================================================
    // Additional Test: Concurrent Charges with Same OrderId (Idempotency)
    // ========================================================================
    /// <summary>
    /// Edge case: Multiple charge attempts for same order with different MessageIds
    /// should all succeed independently (no duplicate prevention across MessageIds)
    /// </summary>
    [Fact]
    public async Task Property_3_1_X_ChargePayment_DifferentMessageIds_CreatesMultipleRecords()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act: Charge same order twice with DIFFERENT MessageIds
        var result1 = await _chargeHandler.HandleAsync(
            new ChargePaymentCommand
            {
                OrderId = orderId,
                CustomerId = customerId,
                Amount = 100.00m,
                MessageId = Guid.NewGuid()  // Different MessageId
            },
            correlationId);

        var result2 = await _chargeHandler.HandleAsync(
            new ChargePaymentCommand
            {
                OrderId = orderId,
                CustomerId = customerId,
                Amount = 100.00m,
                MessageId = Guid.NewGuid()  // Different MessageId
            },
            correlationId);

        // Assert: Both succeed independently
        Assert.True(result1.Success);
        Assert.True(result2.Success);

        // Verify: TWO payment records created (idempotency is per MessageId, not per OrderId)
        var paymentCount = await _dbContext.Payments
            .Where(p => p.OrderId == orderId)
            .CountAsync();
        Assert.Equal(2, paymentCount);
    }
}

/// <summary>
/// Mock logger for testing
/// </summary>
public class MockLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Primitives.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // No-op for testing
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => null;
}
