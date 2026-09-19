using Microsoft.EntityFrameworkCore;
using Payment.Service.Data;
using Payment.Service.Domain;
using Payment.Service.Entities;
using Payment.Service.Handlers;
using Xunit;

namespace Payment.Service.Tests;

/// <summary>
/// Tests for ChargePayment command handler
/// </summary>
public class ChargePaymentHandlerTests : IAsyncLifetime
{
    private PaymentDbContext _dbContext = null!;
    private ChargePaymentHandler _handler = null!;
    private IPaymentFailureInjector _failureInjector = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new PaymentDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _failureInjector = new NoOpPaymentFailureInjector();  // Default: always succeed
        _handler = new ChargePaymentHandler(_dbContext, _failureInjector, new MockLogger<ChargePaymentHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Charge Success
    // ========================================================================
    [Fact]
    public async Task Charge_WithValidCommand_Succeeds()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var command = new ChargePaymentCommand
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 99.99m,
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(99.99m, result.Amount);
        Assert.False(result.IsIdempotent);

        // Verify payment record created
        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.MessageId == messageId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Charged, payment.Status);
        Assert.NotNull(payment.TransactionId);
    }

    // ========================================================================
    // Test: Charge with Failure Injection
    // ========================================================================
    [Fact]
    public async Task Charge_WithFailureInjection_Fails()
    {
        // Arrange
        var failingInjector = new AlwaysFailPaymentFailureInjector();
        var handler = new ChargePaymentHandler(_dbContext, failingInjector, new MockLogger<ChargePaymentHandler>());

        var command = new ChargePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 50.00m,
            MessageId = Guid.NewGuid()
        };

        // Act
        var result = await handler.HandleAsync(command, Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Contains("failed", result.Reason.ToLower());

        // Verify failed payment record created
        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.MessageId == command.MessageId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    // ========================================================================
    // Test: Charge Idempotent
    // ========================================================================
    [Fact]
    public async Task Charge_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var command = new ChargePaymentCommand
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
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

        // Verify only one payment created
        var count = await _dbContext.Payments
            .CountAsync(p => p.MessageId == messageId);
        Assert.Equal(1, count);
    }

    // ========================================================================
    // Test: Charge Validation
    // ========================================================================
    [Fact]
    public async Task Charge_WithNegativeAmount_ThrowsException()
    {
        // Arrange
        var command = new ChargePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = -10m,  // Invalid
            MessageId = Guid.NewGuid()
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(command, Guid.NewGuid()));
    }

    // ========================================================================
    // Test: Charge with Probabilistic Failure
    // ========================================================================
    [Fact]
    public async Task Charge_WithProbabilisticFailure_FailsWithCorrectProbability()
    {
        // Arrange - 50% failure rate with fixed seed for reproducibility
        var probabilisticInjector = new ConfigurablePaymentFailureInjector(failureRate: 0.5, seed: 42);
        var handler = new ChargePaymentHandler(_dbContext, probabilisticInjector, new MockLogger<ChargePaymentHandler>());

        int successCount = 0;
        int failureCount = 0;

        // Act - Run multiple charges
        for (int i = 0; i < 100; i++)
        {
            var command = new ChargePaymentCommand
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = 10.00m,
                MessageId = Guid.NewGuid()
            };

            var result = await handler.HandleAsync(command, Guid.NewGuid());
            if (result.Success)
                successCount++;
            else
                failureCount++;
        }

        // Assert - Should have approximately 50/50 split
        // Allow some variance due to randomness
        Assert.True(successCount > 30 && successCount < 70, $"Success: {successCount}, Expected ~50");
        Assert.True(failureCount > 30 && failureCount < 70, $"Failures: {failureCount}, Expected ~50");
    }

    // ========================================================================
    // Test: Charge with Different Amounts
    // ========================================================================
    [Fact]
    public async Task Charge_WithVariousAmounts_SucceedsForAll()
    {
        // Arrange
        var amounts = new[] { 0.01m, 1.00m, 100.00m, 9999.99m };

        // Act & Assert
        foreach (var amount in amounts)
        {
            var command = new ChargePaymentCommand
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = amount,
                MessageId = Guid.NewGuid()
            };

            var result = await _handler.HandleAsync(command, Guid.NewGuid());
            Assert.True(result.Success);
            Assert.Equal(amount, result.Amount);
        }
    }

    // ========================================================================
    // Test: Charge Records Correlation ID
    // ========================================================================
    [Fact]
    public async Task Charge_RecordsCorrelationIdForTracing()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var command = new ChargePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 50.00m,
            MessageId = messageId
        };

        // Act
        await _handler.HandleAsync(command, correlationId);

        // Assert
        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.MessageId == messageId);
        Assert.NotNull(payment);
        Assert.Equal(correlationId, payment.CorrelationId);
    }
}

/// <summary>
/// Mock logger for testing
/// </summary>
internal class MockLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) { }
}

