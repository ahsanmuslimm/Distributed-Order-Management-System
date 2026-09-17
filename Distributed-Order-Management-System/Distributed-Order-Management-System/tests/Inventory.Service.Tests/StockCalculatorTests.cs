using Microsoft.EntityFrameworkCore;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Entities;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Tests for stock calculation from immutable ledger
/// </summary>
public class StockCalculatorTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private IStockCalculator _calculator = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _calculator = new StockCalculator(_dbContext, new MockLogger<StockCalculator>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Initial Stock (No Movements)
    // ========================================================================
    [Fact]
    public async Task CalculateStock_WithNoMovements_ReturnsInitialStock()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(100, stock);
    }

    // ========================================================================
    // Test: Stock After Reserve
    // ========================================================================
    [Fact]
    public async Task CalculateStock_AfterReserve_SubtractsFromInitialStock()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var ledgerEntry = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 30,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };
        _dbContext.ReservationLedgers.Add(ledgerEntry);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(70, stock);  // 100 - 30 = 70
    }

    // ========================================================================
    // Test: Stock After Reserve + Release
    // ========================================================================
    [Fact]
    public async Task CalculateStock_AfterReserveAndRelease_ReturnsCorrectBalance()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);

        var reserve = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 50,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };

        var release = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 20,
            Type = LedgerType.Release,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };

        _dbContext.ReservationLedgers.AddRange(reserve, release);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(70, stock);  // 100 - 50 + 20 = 70
    }

    // ========================================================================
    // Test: Stock Ignores Pending Entries
    // ========================================================================
    [Fact]
    public async Task CalculateStock_IgnoresPendingEntries()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);

        var processedReserve = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 30,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };

        var pendingReserve = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 50,  // Should be ignored
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Pending
        };

        _dbContext.ReservationLedgers.AddRange(processedReserve, pendingReserve);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(70, stock);  // 100 - 30 (pending ignored)
    }

    // ========================================================================
    // Test: Has Sufficient Stock - True
    // ========================================================================
    [Fact]
    public async Task HasSufficientStock_WithEnoughStock_ReturnsTrue()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _calculator.HasSufficientStockAsync(productId, 50);

        // Assert
        Assert.True(result);
    }

    // ========================================================================
    // Test: Has Sufficient Stock - False
    // ========================================================================
    [Fact]
    public async Task HasSufficientStock_WithInsufficientStock_ReturnsFalse()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);

        var reserve = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 80,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };

        _dbContext.ReservationLedgers.Add(reserve);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _calculator.HasSufficientStockAsync(productId, 50);

        // Assert
        Assert.False(result);  // Available: 20, Requested: 50
    }

    // ========================================================================
    // Test: Stock Never Goes Below Zero
    // ========================================================================
    [Fact]
    public async Task CalculateStock_NeverBelowZero()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 10
        };
        _dbContext.Products.Add(product);

        var hugeReserve = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 1000,  // Way more than initial stock
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };

        _dbContext.ReservationLedgers.Add(hugeReserve);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(0, stock);  // Never negative
    }

    // ========================================================================
    // Test: Multiple Reserves Cumulative
    // ========================================================================
    [Fact]
    public async Task CalculateStock_MultipleReserves_Cumulative()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Test Product",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);

        var ledgers = new[]
        {
            new ReservationLedger { LedgerId = Guid.NewGuid(), ProductId = productId, OrderId = Guid.NewGuid(), Quantity = 10, Type = LedgerType.Reserve, MessageId = Guid.NewGuid(), Status = LedgerStatus.Processed },
            new ReservationLedger { LedgerId = Guid.NewGuid(), ProductId = productId, OrderId = Guid.NewGuid(), Quantity = 20, Type = LedgerType.Reserve, MessageId = Guid.NewGuid(), Status = LedgerStatus.Processed },
            new ReservationLedger { LedgerId = Guid.NewGuid(), ProductId = productId, OrderId = Guid.NewGuid(), Quantity = 30, Type = LedgerType.Reserve, MessageId = Guid.NewGuid(), Status = LedgerStatus.Processed },
        };

        _dbContext.ReservationLedgers.AddRange(ledgers);
        await _dbContext.SaveChangesAsync();

        // Act
        var stock = await _calculator.CalculateStockAsync(productId);

        // Assert
        Assert.Equal(40, stock);  // 100 - (10+20+30) = 40
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
