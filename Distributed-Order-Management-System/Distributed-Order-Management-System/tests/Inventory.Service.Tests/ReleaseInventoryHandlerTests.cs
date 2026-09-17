using Microsoft.EntityFrameworkCore;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Entities;
using Inventory.Service.Handlers;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Tests for ReleaseInventory command handler (Compensation)
/// </summary>
public class ReleaseInventoryHandlerTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private ReleaseInventoryHandler _handler = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _handler = new ReleaseInventoryHandler(_dbContext, new MockLogger<ReleaseInventoryHandler>());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // Test: Release Success (Compensation)
    // ========================================================================
    [Fact]
    public async Task Release_AfterReserve_Succeeds()
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
        
        // Create a processed reserve entry
        var reserveEntry = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = orderId,
            Quantity = 50,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };
        _dbContext.ReservationLedgers.Add(reserveEntry);
        await _dbContext.SaveChangesAsync();

        var command = new ReleaseInventoryCommand
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

        // Verify release ledger entry was created
        var releaseEntry = await _dbContext.ReservationLedgers
            .FirstOrDefaultAsync(rl => rl.MessageId == messageId);
        Assert.NotNull(releaseEntry);
        Assert.Equal(LedgerType.Release, releaseEntry.Type);
        Assert.Equal(LedgerStatus.Processed, releaseEntry.Status);
    }

    // ========================================================================
    // Test: Release Without Prior Reserve (Still Succeeds - Audit Trail)
    // ========================================================================
    [Fact]
    public async Task Release_WithoutPriorReserve_StillSucceeds()
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

        var command = new ReleaseInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 50,
            MessageId = messageId
        };

        // Act
        var result = await _handler.HandleAsync(command, correlationId);

        // Assert
        Assert.True(result.Success);  // Succeeds for audit trail
        
        // Verify release entry created even without reservation
        var releaseEntry = await _dbContext.ReservationLedgers
            .FirstOrDefaultAsync(rl => rl.MessageId == messageId);
        Assert.NotNull(releaseEntry);
        Assert.Equal(LedgerType.Release, releaseEntry.Type);
    }

    // ========================================================================
    // Test: Release Idempotent
    // ========================================================================
    [Fact]
    public async Task Release_WithDuplicateMessageId_IsIdempotent()
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

        var command = new ReleaseInventoryCommand
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

        // Verify only one release entry
        var count = await _dbContext.ReservationLedgers
            .CountAsync(rl => rl.MessageId == messageId);
        Assert.Equal(1, count);
    }

    // ========================================================================
    // Test: Release with Validation
    // ========================================================================
    [Fact]
    public async Task Release_WithNegativeQuantity_ThrowsException()
    {
        // Arrange
        var command = new ReleaseInventoryCommand
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

    // ========================================================================
    // Test: Release Multiple Partial Quantities
    // ========================================================================
    [Fact]
    public async Task Release_MultiplePartialQuantities_Accumulates()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        
        // Create a reserve for 50
        var reserveEntry = new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = orderId,
            Quantity = 50,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        };
        _dbContext.ReservationLedgers.Add(reserveEntry);
        await _dbContext.SaveChangesAsync();

        // Act - Release in two parts
        var command1 = new ReleaseInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 30,
            MessageId = Guid.NewGuid()
        };
        
        var command2 = new ReleaseInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 20,
            MessageId = Guid.NewGuid()
        };

        var result1 = await _handler.HandleAsync(command1, correlationId);
        var result2 = await _handler.HandleAsync(command2, correlationId);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);

        // Verify both release entries
        var releaseEntries = await _dbContext.ReservationLedgers
            .Where(rl => rl.Type == LedgerType.Release && rl.OrderId == orderId)
            .ToListAsync();
        
        Assert.Equal(2, releaseEntries.Count);
        Assert.Equal(30, releaseEntries[0].Quantity);
        Assert.Equal(20, releaseEntries[1].Quantity);
    }
}

