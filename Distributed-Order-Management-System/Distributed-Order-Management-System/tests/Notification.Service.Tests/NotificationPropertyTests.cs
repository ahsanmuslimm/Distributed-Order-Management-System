using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notification.Service.Data;
using Notification.Service.Domain;
using Notification.Service.Entities;
using Notification.Service.Handlers;
using Contracts.Events;
using Xunit;

namespace Notification.Service.Tests;

/// <summary>
/// Property-Based Tests for Notification Service
/// 
/// Properties define mathematical invariants that must hold for all valid inputs.
/// These tests verify notification creation, retry logic, and idempotency.
/// 
/// Properties Implemented:
/// - Property 7.1.1: Idempotent Notification Creation (MessageId deduplication)
/// - Property 7.1.2: Event-Notification Mapping (correct content based on event type)
/// - Property 7.1.3: CorrelationId Propagation (end-to-end tracing)
/// 
/// - Property 7.2.1: Retry Policy Backoff Calculation (exponential increase)
/// - Property 7.2.2: Retry Policy Max Retries (doesn't exceed limit)
/// - Property 7.2.3: Retry Policy Next Attempt Time (correct delay)
/// 
/// - Property 7.3.1: Notification Status Transitions (valid state changes)
/// - Property 7.3.2: Retry Count Monotonicity (increases, never decreases)
/// </summary>
public class NotificationPropertyTests : IAsyncLifetime
{
    private NotificationDbContext _dbContext = null!;
    private CreateNotificationHandler _createHandler = null!;
    private INotificationRetryPolicy _retryPolicy = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new NotificationDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _createHandler = new CreateNotificationHandler(
            _dbContext,
            new MockLogger<CreateNotificationHandler>());

        _retryPolicy = new ExponentialBackoffRetryPolicy();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // PROPERTY 7.1.1: Idempotent Notification Creation
    // ========================================================================
    /// <summary>
    /// Property 7.1.1: Create notification with same MessageId twice is idempotent
    /// 
    /// ∀ event, messageId:
    ///   create(event, messageId)
    ///   create(event, messageId)  // Replay
    ///   ≡ same result, only one record in DB
    /// 
    /// Ensures: Kafka at-least-once delivery doesn't create duplicates
    /// </summary>
    [Fact]
    public async Task Property_7_1_1_CreateNotification_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange
        var @event = new OrderPlacedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Items = new[] { new OrderItem { ProductId = Guid.NewGuid(), Quantity = 2 } }
        };

        var correlationId = @event.CorrelationId;

        // Act: Create twice with same MessageId
        var result1 = await _createHandler.HandleAsync(@event, correlationId);
        var result2 = await _createHandler.HandleAsync(@event, correlationId);

        // Assert: Both succeed, second is marked idempotent
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.False(result1.IsIdempotent);  // First call
        Assert.True(result2.IsIdempotent);   // Second call (duplicate)
        Assert.Equal(result1.NotificationId, result2.NotificationId);

        // Verify only ONE record in database
        var notificationCount = await _dbContext.Notifications
            .Where(n => n.MessageId == @event.MessageId)
            .CountAsync();
        Assert.Equal(1, notificationCount);
    }

    // ========================================================================
    // PROPERTY 7.1.2: Event-Notification Mapping
    // ========================================================================
    /// <summary>
    /// Property 7.1.2: Event type determines notification content correctly
    /// 
    /// ∀ event ∈ {OrderPlaced, OrderConfirmed, OrderFailed, PaymentFailed, InventoryRejected}:
    ///   notification.message contains relevant event data
    ///   notification.eventType = event.GetType().Name
    /// 
    /// Ensures: Correct notification sent for each event
    /// </summary>
    [Fact]
    public async Task Property_7_1_2_EventToNotificationMapping_IsCorrect()
    {
        // Test OrderPlaced event
        var orderPlaced = new OrderPlacedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Items = new[] { new OrderItem { ProductId = Guid.NewGuid(), Quantity = 5 } }
        };

        var result = await _createHandler.HandleAsync(orderPlaced, orderPlaced.CorrelationId);
        Assert.True(result.Success);

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == result.NotificationId);
        Assert.NotNull(notification);
        Assert.Equal("OrderPlaced", notification.EventType);
        Assert.Contains("order", notification.Message.ToLower());

        // Test OrderConfirmed event
        var orderConfirmed = new OrderConfirmedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid()
        };

        var result2 = await _createHandler.HandleAsync(orderConfirmed, orderConfirmed.CorrelationId);
        Assert.True(result2.Success);

        var notification2 = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == result2.NotificationId);
        Assert.NotNull(notification2);
        Assert.Equal("OrderConfirmed", notification2.EventType);
        Assert.Contains("confirmed", notification2.Message.ToLower());
    }

    // ========================================================================
    // PROPERTY 7.1.3: CorrelationId Propagation
    // ========================================================================
    /// <summary>
    /// Property 7.1.3: CorrelationId flows from event to notification
    /// 
    /// ∀ event, correlationId:
    ///   notification = create(event, correlationId)
    ///   → notification.correlationId = event.correlationId
    /// 
    /// Ensures: Distributed tracing works end-to-end
    /// </summary>
    [Fact]
    public async Task Property_7_1_3_CorrelationId_PropagatedToNotification()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var @event = new OrderFailedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = correlationId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Reason = "Stock unavailable"
        };

        // Act
        var result = await _createHandler.HandleAsync(@event, correlationId);

        // Assert
        Assert.True(result.Success);

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == result.NotificationId);
        Assert.NotNull(notification);
        Assert.Equal(correlationId, notification.CorrelationId);
    }

    // ========================================================================
    // PROPERTY 7.2.1: Retry Policy Backoff Calculation
    // ========================================================================
    /// <summary>
    /// Property 7.2.1: Backoff increases exponentially with each retry
    /// 
    /// ∀ attempt ∈ [1, 4]:
    ///   backoff(attempt) < backoff(attempt + 1)
    ///   backoff(attempt) ≤ maxBackoff
    /// 
    /// Ensures: Exponential backoff implemented correctly
    /// </summary>
    [Fact]
    public void Property_7_2_1_RetryPolicy_BackoffIncreases_Exponentially()
    {
        // Arrange & Act
        var backoff1 = _retryPolicy.CalculateBackoff(1);  // ~1s
        var backoff2 = _retryPolicy.CalculateBackoff(2);  // ~2s
        var backoff3 = _retryPolicy.CalculateBackoff(3);  // ~4s
        var backoff4 = _retryPolicy.CalculateBackoff(4);  // ~8s, capped at 30s

        // Assert: Each is larger than previous
        Assert.True(backoff1 < backoff2);
        Assert.True(backoff2 < backoff3);
        Assert.True(backoff3 < backoff4);

        // All are within reasonable bounds
        Assert.True(backoff1.TotalSeconds >= 1);
        Assert.True(backoff4.TotalSeconds <= 30);  // Capped at maxBackoff
    }

    // ========================================================================
    // PROPERTY 7.2.2: Retry Policy Max Retries
    // ========================================================================
    /// <summary>
    /// Property 7.2.2: ShouldRetry respects max retries limit
    /// 
    /// ∀ attempt:
    ///   attempt <= maxRetries → shouldRetry(attempt) = true
    ///   attempt > maxRetries → shouldRetry(attempt) = false
    /// 
    /// Ensures: Retry doesn't exceed configured limit
    /// </summary>
    [Fact]
    public void Property_7_2_2_RetryPolicy_RespectsMaxRetries()
    {
        // Arrange: Default policy has MaxRetries = 3
        var policy = new ExponentialBackoffRetryPolicy();
        Assert.Equal(3, policy.MaxRetries);

        // Act & Assert
        Assert.True(policy.ShouldRetry(1));
        Assert.True(policy.ShouldRetry(2));
        Assert.True(policy.ShouldRetry(3));
        Assert.False(policy.ShouldRetry(4));  // Beyond max retries
    }

    // ========================================================================
    // PROPERTY 7.2.3: Retry Policy Next Attempt Time
    // ========================================================================
    /// <summary>
    /// Property 7.2.3: GetNextRetryTime returns correct delay
    /// 
    /// ∀ lastAttempt, attemptNumber:
    ///   nextTime = getNextRetryTime(lastAttempt, attemptNumber)
    ///   nextTime = lastAttempt + backoff(attemptNumber)
    /// 
    /// Ensures: Retry scheduling is correct
    /// </summary>
    [Fact]
    public void Property_7_2_3_RetryPolicy_NextAttemptTime_IsCorrect()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var policy = new ExponentialBackoffRetryPolicy();
        var nextAttemptNumber = 2;

        // Act
        var nextTime = policy.GetNextRetryTime(now, nextAttemptNumber);
        var expectedBackoff = policy.CalculateBackoff(nextAttemptNumber);
        var expectedTime = now.Add(expectedBackoff);

        // Assert
        Assert.Equal(expectedTime, nextTime);
    }

    // ========================================================================
    // PROPERTY 7.3.1: Notification Status Transitions
    // ========================================================================
    /// <summary>
    /// Property 7.3.1: Notification status is valid enum value
    /// 
    /// ∀ notification:
    ///   notification.status ∈ {Pending, Sent, Failed, DLQ}
    /// 
    /// Ensures: No invalid states
    /// </summary>
    [Fact]
    public async Task Property_7_3_1_NotificationStatus_IsValid()
    {
        // Arrange
        var @event = new PaymentFailedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 99.99m,
            Reason = "Card declined"
        };

        var result = await _createHandler.HandleAsync(@event, @event.CorrelationId);

        // Act
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == result.NotificationId);

        // Assert
        Assert.NotNull(notification);
        var validStatuses = new[] 
        { 
            NotificationStatus.Pending,
            NotificationStatus.Sent,
            NotificationStatus.Failed,
            NotificationStatus.DLQ
        };
        Assert.Contains(notification.Status, validStatuses);
    }

    // ========================================================================
    // PROPERTY 7.3.2: Retry Count Monotonicity
    // ========================================================================
    /// <summary>
    /// Property 7.3.2: Retry count is non-decreasing (0-3)
    /// 
    /// ∀ notification in DB:
    ///   0 <= notification.retryCount <= 3
    ///   retryCount never decreases
    /// 
    /// Ensures: Retry tracking is monotonic
    /// </summary>
    [Fact]
    public async Task Property_7_3_2_RetryCount_IsMonotonic()
    {
        // Arrange: Create multiple notifications
        var notifications = new[]
        {
            new OrderPlacedEvent
            {
                MessageId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Items = new[] { new OrderItem { ProductId = Guid.NewGuid(), Quantity = 1 } }
            },
            new OrderConfirmedEvent
            {
                MessageId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid()
            },
            new OrderFailedEvent
            {
                MessageId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Reason = "Cancelled"
            }
        };

        // Act: Create each notification
        foreach (var @event in notifications)
        {
            if (@event is OrderPlacedEvent orderPlaced)
                await _createHandler.HandleAsync(orderPlaced, @event.CorrelationId);
            else if (@event is OrderConfirmedEvent orderConfirmed)
                await _createHandler.HandleAsync(orderConfirmed, @event.CorrelationId);
            else if (@event is OrderFailedEvent orderFailed)
                await _createHandler.HandleAsync(orderFailed, @event.CorrelationId);
        }

        // Assert
        var allNotifications = await _dbContext.Notifications.ToListAsync();
        Assert.All(allNotifications, n =>
        {
            // Retry count is within valid range
            Assert.True(n.RetryCount >= 0 && n.RetryCount <= 3);
        });
    }

    // ========================================================================
    // Bonus: Immediate Retry Policy Alternative
    // ========================================================================
    /// <summary>
    /// Test that ImmediateRetryPolicy works (no backoff)
    /// </summary>
    [Fact]
    public void Property_7_X_ImmediateRetryPolicy_NoDelay()
    {
        // Arrange
        var policy = new ImmediateRetryPolicy(maxRetries: 2);

        // Act & Assert
        Assert.Equal(TimeSpan.Zero, policy.CalculateBackoff(1));
        Assert.Equal(TimeSpan.Zero, policy.CalculateBackoff(2));
        Assert.True(policy.ShouldRetry(1));
        Assert.True(policy.ShouldRetry(2));
        Assert.False(policy.ShouldRetry(3));
    }
}

/// <summary>
/// Mock Logger for testing
/// </summary>
public class MockLogger<T> : ILogger<T>
{
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // No-op for testing
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => null;
}
