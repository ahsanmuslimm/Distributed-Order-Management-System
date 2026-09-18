using Microsoft.EntityFrameworkCore;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Entities;
using Inventory.Service.Handlers;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Tests for ReserveInventory command handler
/// </summary>
public class ReserveInventoryHandlerTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private ReserveInventoryHandler _handler = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        var calculator = new StockCalculator(_dbContext, new MockLogger<StockCalculator>());
        _handler = new ReserveInventoryHandler(_dbContext, calculator, new MockLogger<ReserveInventoryHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Reserve Success
    // ========================================================================
    [Fact]
    public async Task Reserve_WithSufficientStock_Succeeds()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 50,
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(50, result.Quantity);
        Assert.False(result.IsIdempotent);

        // Verify ledger entry
        var ledgerEntry = await _dbContext.ReservationLedgers
            .FirstOrDefaultAsync(rl => rl.MessageId == messageId);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(LedgerStatus.Processed, ledgerEntry.Status);
    }

    // ========================================================================
    // Test: Reserve Insufficient Stock
    // ========================================================================
    [Fact]
    public async Task Reserve_WithInsufficientStock_Fails()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 10
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 50,  // More than available
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Insufficient stock", result.Reason);

        // Verify rejected ledger entry
        var ledgerEntry = await _dbContext.ReservationLedgers
            .FirstOrDefaultAsync(rl => rl.MessageId == messageId);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(LedgerStatus.Rejected, ledgerEntry.Status);
    }

    // ========================================================================
    // Test: Reserve Idempotent
    // ========================================================================
    [Fact]
    public async Task Reserve_WithDuplicateMessageId_IsIdempotent()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 50,
            MessageId = messageId
        };

        // Act - First call
        var result1 = await _handler.HandleAsync(command, correlationId);
        var result2 = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.False(result1.IsIdempotent);
        Assert.True(result2.IsIdempotent);

        // Verify only one ledger entry
        var count = await _dbContext.ReservationLedgers
            .CountAsync(rl => rl.MessageId == messageId);
        Assert.Equal(1, count);
    }

    // ========================================================================
    // Test: Reserve With Existing Reservations
    // ========================================================================
    [Fact]
    public async Task Reserve_ConsidersExistingReservations()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Existing reservation of 60
        var existingLedger = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 60,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };
        _dbContext.ReservationLedgers.Add(existingLedger);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = 50,  // Needs 50, but only 40 available
            MessageId = Guid.NewGuid()
        };

        // Act
        var result = await _handler.HandleAsync(command, Guid.NewGuid());

        // Assert
        Assert.False(result.Success);  // Should fail: 100-60=40 available, needs 50
    }

    // ========================================================================
    // Test: Reserve Validation
    // ========================================================================
    [Fact]
    public async Task Reserve_WithNegativeQuantity_ThrowsException()
    {
        // Arrange
        var command = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = -10,  // Invalid
            MessageId = Guid.NewGuid()
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(command, Guid.NewGuid()));
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
