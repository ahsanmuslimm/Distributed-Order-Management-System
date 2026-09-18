using Microsoft.EntityFrameworkCore;
using Orders.Service.Data;
using Orders.Service.Entities;
using Orders.Service.Handlers;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Integration tests for Get Order Status endpoint
/// Tests query behavior and response mapping
/// </summary>
public class GetOrderStatusEndpointTests : IAsyncLifetime
{
    private OrderDbContext _dbContext = null!;
    private GetOrderStatusHandler _handler = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new OrderDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _handler = new GetOrderStatusHandler(_dbContext, new MockLogger<GetOrderStatusHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Get Order Status - Success
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_WithValidOrderId_ReturnsOrder()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var order = new Order
        {
            OrderId = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            TotalAmount = 100.00m,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    Quantity = 5,
                    UnitPrice = 20.00m
                }
            }
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _handler.HandleAsync(orderId, correlationId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(customerId, result.CustomerId);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(100.00m, result.TotalAmount);
        Assert.Single(result.Items);
    }

    // ========================================================================
    // Test: Get Order Status - Includes Status History
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_Includes_StatusHistory()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var order = new Order
        {
            OrderId = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Confirmed,
            TotalAmount = 100.00m
        };

        var transition1 = new OrderStatusTransition
        {
            TransitionId = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = null,
            ToStatus = OrderStatus.Pending,
            Reason = "Order created",
            CorrelationId = correlationId
        };

        var transition2 = new OrderStatusTransition
        {
            TransitionId = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Reserved,
            Reason = "Inventory reserved",
            CorrelationId = correlationId
        };

        _dbContext.Orders.Add(order);
        _dbContext.OrderStatusTransitions.AddRange(transition1, transition2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _handler.HandleAsync(orderId, correlationId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.StatusHistory.Length);
        Assert.Equal("Pending", result.StatusHistory[0].ToStatus);
        Assert.Equal("Reserved", result.StatusHistory[1].ToStatus);
    }

    // ========================================================================
    // Test: Get Order Status - Not Found
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_WithNonExistentOrderId_ReturnsNull()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await _handler.HandleAsync(orderId, correlationId);

        // Assert
        Assert.Null(result);
    }

    // ========================================================================
    // Test: Get Order Status - Multiple Items Included
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_WithMultipleItems_ReturnsAllItems()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var order = new Order
        {
            OrderId = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            TotalAmount = 250.00m,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    Quantity = 2,
                    UnitPrice = 50.00m
                },
                new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    Quantity = 3,
                    UnitPrice = 50.00m
                }
            }
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _handler.HandleAsync(orderId, correlationId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Length);
        Assert.Equal(100.00m, result.Items[0].LineTotal);
        Assert.Equal(150.00m, result.Items[1].LineTotal);
    }

    // ========================================================================
    // Test: Get Order Status - Validation: Empty OrderId
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_WithEmptyOrderId_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(Guid.Empty, correlationId));
    }

    // ========================================================================
    // Test: Get Order Status - Response DTO Contains All Fields
    // ========================================================================
    [Fact]
    public async Task GetOrderStatus_ResponseDTO_ContainsAllFields()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var order = new Order
        {
            OrderId = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Charged,
            TotalAmount = 99.99m,
            SagaId = sagaId,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            LastUpdatedAt = DateTime.UtcNow
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _handler.HandleAsync(orderId, correlationId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(customerId, result.CustomerId);
        Assert.Equal("Charged", result.Status);
        Assert.Equal(99.99m, result.TotalAmount);
        Assert.Equal(sagaId, result.SagaId);
        Assert.NotEqual(default, result.CreatedAt);
        Assert.NotEqual(default, result.LastUpdatedAt);
        Assert.NotNull(result.Items);
        Assert.NotNull(result.StatusHistory);
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
