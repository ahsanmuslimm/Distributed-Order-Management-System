using Microsoft.EntityFrameworkCore;
using Orders.Service.Data;
using Orders.Service.Infrastructure;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Integration tests for Inbox Pattern implementation
/// Tests deduplication and idempotency
/// </summary>
public class InboxProcessorTests : IAsyncLifetime
{
    private OrderDbContext _dbContext = null!;
    private InboxProcessor _processor = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new OrderDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _processor = new InboxProcessor(_dbContext, new MockLogger<InboxProcessor>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Inbox - New Message Not Yet Processed
    // ========================================================================
    [Fact]
    public async Task HasProcessed_WithNewMessageId_ReturnsFalse()
    {
        // Arrange
        var messageId = Guid.NewGuid();

        // Act
        var hasProcessed = await _processor.HasProcessedAsync(messageId);

        // Assert
        Assert.False(hasProcessed);
    }

    // ========================================================================
    // Test: Inbox - Record Message, Then Check
    // ========================================================================
    [Fact]
    public async Task HasProcessed_AfterRecording_ReturnsTrue()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act - Record message
        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "TestCommand",
            new { Data = "test" },
            correlationId);

        // Act - Check if processed
        var hasProcessed = await _processor.HasProcessedAsync(messageId);

        // Assert
        Assert.True(hasProcessed);
    }

    // ========================================================================
    // Test: Inbox - Message Details Stored Correctly
    // ========================================================================
    [Fact]
    public async Task RecordProcessed_StoresMessageDetailsCorrectly()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var payload = new { CustomerId = Guid.NewGuid(), Amount = 100.00m };

        // Act
        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "PaymentChargeCommand",
            payload,
            correlationId);

        // Assert - Verify in database
        var recorded = await _dbContext.InboxMessages.FindAsync(messageId);
        Assert.NotNull(recorded);
        Assert.Equal(orderId, recorded.OrderId);
        Assert.Equal("PaymentChargeCommand", recorded.MessageType);
        Assert.Equal(correlationId, recorded.CorrelationId);
        Assert.NotNull(recorded.Payload);  // JSON serialized
    }

    // ========================================================================
    // Test: Inbox - Duplicate MessageId Prevention
    // ========================================================================
    [Fact]
    public async Task RecordProcessed_WithDuplicateMessageId_StillIdempotent()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act - First recording
        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "TestCommand",
            new { Data = "first" },
            correlationId);

        // Act - Try to record same MessageId again
        // Should not throw (caught as duplicate, logged as warning)
        var count = _dbContext.InboxMessages.Count();

        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "TestCommand",
            new { Data = "second" },
            correlationId);

        // Assert - Only one record for this MessageId
        var newCount = _dbContext.InboxMessages.Count();
        Assert.Equal(count, newCount);  // No new record added (or exception handled)

        // Verify only one exists
        var exists = await _dbContext.InboxMessages
            .Where(im => im.MessageId == messageId)
            .CountAsync();
        Assert.Equal(1, exists);
    }

    // ========================================================================
    // Test: Inbox - Multiple Messages Can Coexist
    // ========================================================================
    [Fact]
    public async Task Inbox_MultipleMessages_AreIndependent()
    {
        // Arrange
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act
        await _processor.RecordProcessedAsync(messageId1, orderId, "Cmd1", new { }, correlationId);
        await _processor.RecordProcessedAsync(messageId2, orderId, "Cmd2", new { }, correlationId);

        // Assert
        var hasProcessed1 = await _processor.HasProcessedAsync(messageId1);
        var hasProcessed2 = await _processor.HasProcessedAsync(messageId2);

        Assert.True(hasProcessed1);
        Assert.True(hasProcessed2);

        var count = await _dbContext.InboxMessages.CountAsync();
        Assert.Equal(2, count);
    }

    // ========================================================================
    // Test: Inbox - Validation: Empty MessageId
    // ========================================================================
    [Fact]
    public async Task HasProcessed_WithEmptyMessageId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.HasProcessedAsync(Guid.Empty));
    }

    // ========================================================================
    // Test: Inbox - Validation: Empty OrderId
    // ========================================================================
    [Fact]
    public async Task RecordProcessed_WithEmptyOrderId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.RecordProcessedAsync(
                Guid.NewGuid(),
                Guid.Empty,  // Invalid
                "Test",
                new { },
                Guid.NewGuid()));
    }

    // ========================================================================
    // Test: Inbox - Validation: Empty MessageType
    // ========================================================================
    [Fact]
    public async Task RecordProcessed_WithEmptyMessageType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.RecordProcessedAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "",  // Invalid
                new { },
                Guid.NewGuid()));
    }

    // ========================================================================
    // Test: Inbox Pattern Prevents Double Processing
    // ========================================================================
    [Fact]
    public async Task InboxPattern_PreventsDoubleProcessing()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // First attempt: Message not yet processed
        var hasProcessed1 = await _processor.HasProcessedAsync(messageId);

        // Record as processed
        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "Command",
            new { },
            correlationId);

        // Second attempt: Message already processed
        var hasProcessed2 = await _processor.HasProcessedAsync(messageId);

        // Assert
        Assert.False(hasProcessed1);  // First time, not processed
        Assert.True(hasProcessed2);   // Second time, already processed (idempotent)
    }

    // ========================================================================
    // Test: Inbox - Correlation ID Preserved
    // ========================================================================
    [Fact]
    public async Task RecordProcessed_PreservesCorrelationId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act
        await _processor.RecordProcessedAsync(
            messageId,
            orderId,
            "Test",
            new { },
            correlationId);

        // Assert
        var recorded = await _dbContext.InboxMessages.FindAsync(messageId);
        Assert.Equal(correlationId, recorded!.CorrelationId);
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
        Func<TState, Exception?, string> formatter)
    {
        // Silent
    }
}
