using Microsoft.EntityFrameworkCore;
using Orders.Service.Data;
using Orders.Service.DTOs;
using Orders.Service.Handlers;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Integration tests for Place Order endpoint
/// Tests HTTP behavior, business logic, and database interaction
/// </summary>
public class PlaceOrderEndpointTests : IAsyncLifetime
{
    private OrderDbContext _dbContext = null!;
    private PlaceOrderHandler _handler = null!;

    public async Task InitializeAsync()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new OrderDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _handler = new PlaceOrderHandler(_dbContext, new MockLogger<PlaceOrderHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Place Order - Success
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithValidRequest_CreatesOrderInDatabase()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = customerId,
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 5,
                    UnitPrice = 10.00m
                },
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 3,
                    UnitPrice = 20.00m
                }
            }
        };

        // Act
        var result = await _handler.HandleAsync(request, correlationId);

        // Assert
        Assert.NotEqual(Guid.Empty, result.OrderId);
        Assert.Equal(customerId, result.CustomerId);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(110.00m, result.TotalAmount);  // (5*10) + (3*20)
        Assert.Equal(2, result.ItemCount);

        // Verify in database
        var order = await _dbContext.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderId == result.OrderId);
        Assert.NotNull(order);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(110.00m, order.TotalAmount);
    }

    // ========================================================================
    // Test: Place Order - Status Transition Recorded
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_Records_InitialStatusTransition()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = customerId,
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = 100.00m
                }
            }
        };

        // Act
        var result = await _handler.HandleAsync(request, correlationId);

        // Assert
        var transition = await _dbContext.OrderStatusTransitions
            .FirstOrDefaultAsync(st => st.OrderId == result.OrderId);

        Assert.NotNull(transition);
        Assert.Null(transition.FromStatus);  // No previous status
        Assert.Equal("Pending", transition.ToStatus.ToString());
        Assert.Equal("Order created", transition.Reason);
        Assert.Equal(correlationId, transition.CorrelationId);
    }

    // ========================================================================
    // Test: Place Order - Validation: Empty CustomerId
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithEmptyCustomerId_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = Guid.Empty,  // Invalid
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = 100.00m
                }
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(request, correlationId));
        Assert.Contains("CustomerId", ex.Message);
    }

    // ========================================================================
    // Test: Place Order - Validation: No Items
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithNoItems_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = Array.Empty<OrderItemRequest>()  // Invalid
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(request, correlationId));
        Assert.Contains("at least one", ex.Message);
    }

    // ========================================================================
    // Test: Place Order - Validation: Negative Quantity
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithNegativeQuantity_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = -5,  // Invalid
                    UnitPrice = 100.00m
                }
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(request, correlationId));
        Assert.Contains("Quantity", ex.Message);
    }

    // ========================================================================
    // Test: Place Order - Validation: Zero Quantity
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithZeroQuantity_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 0,  // Invalid
                    UnitPrice = 100.00m
                }
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(request, correlationId));
        Assert.Contains("Quantity", ex.Message);
    }

    // ========================================================================
    // Test: Place Order - Validation: Negative Price
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_WithNegativePrice_ThrowsArgumentException()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[]
            {
                new OrderItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = -50.00m  // Invalid
                }
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(request, correlationId));
        Assert.Contains("UnitPrice", ex.Message);
    }

    // ========================================================================
    // Test: Place Order - Total Amount Calculation
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_CalculatesTotalAmountCorrectly()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var request = new OrderRequest
        {
            CustomerId = customerId,
            Items = new[]
            {
                new OrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 10.50m },  // 21.00
                new OrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 3, UnitPrice = 5.75m },   // 17.25
                new OrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 99.99m }   // 99.99
            }
        };

        // Act
        var result = await _handler.HandleAsync(request, correlationId);

        // Assert
        var expected = 21.00m + 17.25m + 99.99m;
        Assert.Equal(expected, result.TotalAmount);
    }

    // ========================================================================
    // Test: Place Order - Multiple Orders Can Be Created
    // ========================================================================
    [Fact]
    public async Task PlaceOrder_MultipleOrders_AreIndependent()
    {
        // Arrange
        var correlationId1 = Guid.NewGuid();
        var correlationId2 = Guid.NewGuid();
        var request1 = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[] { new OrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100.00m } }
        };
        var request2 = new OrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[] { new OrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 50.00m } }
        };

        // Act
        var result1 = await _handler.HandleAsync(request1, correlationId1);
        var result2 = await _handler.HandleAsync(request2, correlationId2);

        // Assert
        Assert.NotEqual(result1.OrderId, result2.OrderId);
        Assert.NotEqual(result1.CustomerId, result2.CustomerId);
        Assert.Equal(100.00m, result1.TotalAmount);
        Assert.Equal(100.00m, result2.TotalAmount);

        // Verify both in database
        var order1 = await _dbContext.Orders.FindAsync(result1.OrderId);
        var order2 = await _dbContext.Orders.FindAsync(result2.OrderId);
        Assert.NotNull(order1);
        Assert.NotNull(order2);
    }
}

/// <summary>
/// Mock logger for testing (doesn't require DI)
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
