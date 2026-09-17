using Microsoft.EntityFrameworkCore;
using Payment.Service.Data;
using Payment.Service.Entities;
using Payment.Service.Handlers;
using Xunit;

namespace Payment.Service.Tests;

/// <summary>
/// Tests for RefundPayment command handler (Compensation)
/// </summary>
public class RefundPaymentHandlerTests : IAsyncLifetime
{
    private PaymentDbContext _dbContext = null!;
    private RefundPaymentHandler _handler = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new PaymentDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _handler = new RefundPaymentHandler(_dbContext, new MockLogger<RefundPaymentHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Refund Success (Compensation)
    // ========================================================================
    [Fact]
    public async Task Refund_AfterCharge_Succeeds()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // First create a charged payment
        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.00m,
            Status = PaymentStatus.Charged,
            TransactionId = "TXN123",
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        var command = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 100.00m,
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(paymentId, result.PaymentId);
        Assert.Equal(100.00m, result.Amount);
        Assert.False(result.IsIdempotent);

        // Verify refund entry created
        var refund = await _dbContext.Refunds
            .FirstOrDefaultAsync(r => r.MessageId == messageId);
        Assert.NotNull(refund);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);  // Payment updated to Refunded
    }

    // ========================================================================
    // Test: Refund Without Prior Charge Still Creates Record (Audit)
    // ========================================================================
    [Fact]
    public async Task Refund_WithoutPriorCharge_StillCreatesRefundRecord()
    {
        // Arrange
        var nonexistentPaymentId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var command = new RefundPaymentCommand
        {
            PaymentId = nonexistentPaymentId,
            Amount = 50.00m,
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.False(result.Success);  // Fails because payment not found
        
        // But refund record created for audit
        var refund = await _dbContext.Refunds
            .FirstOrDefaultAsync(r => r.MessageId == messageId);
        Assert.NotNull(refund);
        Assert.Equal(RefundStatus.Failed, refund.Status);
    }

    // ========================================================================
    // Test: Refund Idempotent
    // ========================================================================
    [Fact]
    public async Task Refund_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 75.00m,
            Status = PaymentStatus.Charged,
            TransactionId = "TXN456",
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        var command = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 75.00m,
            MessageId = messageId
        };

        // Act - First call
        var result1 = await _handler.HandleAsync(command, correlationId);
        var result2 = await _handler.HandleAsync(command, correlationId); // Replay

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.False(result1.IsIdempotent);
        Assert.True(result2.IsIdempotent);

        // Verify only one refund created
        var count = await _dbContext.Refunds
            .CountAsync(r => r.MessageId == messageId);
        Assert.Equal(1, count);
    }

    // ========================================================================
    // Test: Refund With Validation (Negative Amount)
    // ========================================================================
    [Fact]
    public async Task Refund_WithNegativeAmount_ThrowsException()
    {
        // Arrange
        var command = new RefundPaymentCommand
        {
            PaymentId = Guid.NewGuid(),
            Amount = -10m,  // Invalid
            MessageId = Guid.NewGuid()
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(command, Guid.NewGuid()));
    }

    // ========================================================================
    // Test: Refund Cannot Process Failed Payment
    // ========================================================================
    [Fact]
    public async Task Refund_OfFailedPayment_DoesNotSucceed()
    {
        // Arrange
        var paymentId = Guid.NewGuid();

        var failedPayment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 50.00m,
            Status = PaymentStatus.Failed,  // Already failed!
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(failedPayment);
        await _dbContext.SaveChangesAsync();

        var command = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 50.00m,
            MessageId = Guid.NewGuid()
        };

        // Act
        var result = await _handler.HandleAsync(command, Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Contains("status", result.Reason.ToLower());
    }

    // ========================================================================
    // Test: Compensation Round Trip (Reserve + Refund)
    // ========================================================================
    [Fact]
    public async Task Compensation_ChargeAndRefund_RoundTrip()
    {
        // Arrange - Simulate: Charge fails in saga → Compensate with refund
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 150.00m;

        // Create a charged payment (simulating successful charge first)
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentStatus.Charged,
            TransactionId = "TXN789",
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        var refundCmd = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = amount,
            MessageId = Guid.NewGuid()
        };

        // Act
        var refundResult = await _handler.HandleAsync(refundCmd, Guid.NewGuid());

        // Assert
        Assert.True(refundResult.Success);
        
        var refundedPayment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        Assert.NotNull(refundedPayment);
        Assert.Equal(PaymentStatus.Refunded, refundedPayment.Status);  // Refunded!
    }

    // ========================================================================
    // Test: Partial Refund
    // ========================================================================
    [Fact]
    public async Task Refund_PartialAmount_Succeeds()
    {
        // Arrange
        var paymentId = Guid.NewGuid();

        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.00m,  // Original charge
            Status = PaymentStatus.Charged,
            TransactionId = "TXN999",
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        var refundCmd = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 50.00m,  // Partial refund
            MessageId = Guid.NewGuid()
        };

        // Act
        var result = await _handler.HandleAsync(refundCmd, Guid.NewGuid());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50.00m, result.Amount);  // Partial amount refunded

        var refund = await _dbContext.Refunds
            .FirstOrDefaultAsync(r => r.MessageId == refundCmd.MessageId);
        Assert.NotNull(refund);
        Assert.Equal(50.00m, refund.Amount);
    }

    // ========================================================================
    // Test: Multiple Refunds On Same Payment
    // ========================================================================
    [Fact]
    public async Task Refund_MultiplePartialRefunds_Accumulate()
    {
        // Arrange
        var paymentId = Guid.NewGuid();

        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.00m,
            Status = PaymentStatus.Charged,
            TransactionId = "TXN111",
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync();

        // Issue two partial refunds
        var refund1 = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 30.00m,
            MessageId = Guid.NewGuid()
        };

        var refund2 = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = 20.00m,
            MessageId = Guid.NewGuid()
        };

        // Act
        var result1 = await _handler.HandleAsync(refund1, Guid.NewGuid());
        var result2 = await _handler.HandleAsync(refund2, Guid.NewGuid());

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);

        // Verify both refunds created
        var refundCount = await _dbContext.Refunds
            .CountAsync(r => r.PaymentId == paymentId);
        Assert.Equal(2, refundCount);

        var totalRefunded = await _dbContext.Refunds
            .Where(r => r.PaymentId == paymentId)
            .SumAsync(r => r.Amount);
        Assert.Equal(50.00m, totalRefunded);
    }
}

