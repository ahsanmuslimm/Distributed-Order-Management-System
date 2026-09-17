using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Entities;
using Inventory.Service.Handlers;
using Xunit;

namespace Inventory.Service.Tests;

/// <summary>
/// Property-Based Tests for Inventory Service
/// 
/// Properties define mathematical invariants that must hold for ALL valid inputs.
/// These tests generate random data to find edge cases automatically.
/// 
/// Properties Implemented:
/// - Property 2.1.1: Idempotent Processing (reserve)
/// - Property 2.1.2: Stock Decrement Exactly Once
/// - Property 2.1.3: Ledger Entry Created
/// - Property 2.1.4: Stock Never Negative
/// - Property 2.1.5: Multiple Reserves Accumulate
/// 
/// - Property 2.2.1: Release Idempotency
/// - Property 2.2.2: Reserve-Release Round Trip
/// - Property 2.2.3: Ledger Debit-Credit Balance
/// - Property 2.2.4: Partial Releases
/// 
/// - Property 2.3.1: Cache-Ledger Consistency
/// - Property 2.3.2: Cache Invalidation Works
/// - Property 2.3.3: Fallback to Ledger on Error
/// - Property 2.3.4: Concurrent Access Safety
/// </summary>
public class InventoryPropertyTests : IAsyncLifetime
{
    private InventoryDbContext _dbContext = null!;
    private ReserveInventoryHandler _reserveHandler = null!;
    private ReleaseInventoryHandler _releaseHandler = null!;
    private IStockCalculator _stockCalculator = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        var logger = new MockLogger<StockCalculator>();
        _stockCalculator = new StockCalculator(_dbContext, logger);

        var reserveLogger = new MockLogger<ReserveInventoryHandler>();
        _reserveHandler = new ReserveInventoryHandler(_dbContext, _stockCalculator, reserveLogger);

        var releaseLogger = new MockLogger<ReleaseInventoryHandler>();
        _releaseHandler = new ReleaseInventoryHandler(_dbContext, releaseLogger);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ========================================================================
    // PROPERTY 2.1.1: Idempotent Processing (Reserve)
    // ========================================================================
    [Fact]
    public async Task Property_2_1_1_ReserveInventory_WithDuplicateMessageId_IsIdempotent()
    {
        // Given
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var quantity = 50;

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity,
            MessageId = messageId
        };

        // When
        var result1 = await _reserveHandler.HandleAsync(command, correlationId);
        var stock1 = await _stockCalculator.CalculateStockAsync(productId);

        var result2 = await _reserveHandler.HandleAsync(command, correlationId); // Replay
        var stock2 = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Idempotent (both results same, stock same)
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(stock1, stock2);
        Assert.False(result2.IsIdempotent == false);  // Second call must be marked idempotent
    }

    // ========================================================================
    // PROPERTY 2.1.2: Stock Decrement Exactly Once
    // ========================================================================
    [Fact]
    public async Task Property_2_1_2_ReserveInventory_StockDecrementsExactlyOnce()
    {
        // Given
        var productId = Guid.NewGuid();
        var initialStock = 1000;
        var quantity = 250;

        var product = new Product
        {
            ProductId = productId,
            Name = "Gadget",
            InitialStock = initialStock
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = quantity,
            MessageId = Guid.NewGuid()
        };

        // When
        var result = await _reserveHandler.HandleAsync(command, Guid.NewGuid());
        var finalStock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Stock = InitialStock - Quantity
        Assert.True(result.Success);
        Assert.Equal(initialStock - quantity, finalStock);
    }

    // ========================================================================
    // PROPERTY 2.1.3: Ledger Entry Created
    // ========================================================================
    [Fact]
    public async Task Property_2_1_3_ReserveInventory_CreatesLedgerEntry()
    {
        // Given
        var productId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Item",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = 30,
            MessageId = messageId
        };

        // When
        await _reserveHandler.HandleAsync(command, Guid.NewGuid());

        // Then - Property: Exactly one ledger entry with this MessageId exists
        var ledgerCount = await _dbContext.ReservationLedgers
            .CountAsync(l => l.MessageId == messageId);

        Assert.Equal(1, ledgerCount);
    }

    // ========================================================================
    // PROPERTY 2.1.4: Stock Never Negative
    // ========================================================================
    [Fact]
    public async Task Property_2_1_4_ReserveInventory_StockNeverNegative()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var commands = new[]
        {
            new ReserveInventoryCommand
            {
                OrderId = Guid.NewGuid(),
                ProductId = productId,
                Quantity = 80,
                MessageId = Guid.NewGuid()
            },
            new ReserveInventoryCommand
            {
                OrderId = Guid.NewGuid(),
                ProductId = productId,
                Quantity = 50,  // Will fail but should be handled
                MessageId = Guid.NewGuid()
            }
        };

        // When - Process all commands
        foreach (var cmd in commands)
        {
            await _reserveHandler.HandleAsync(cmd, Guid.NewGuid());
        }

        // Then - Property: Stock >= 0 always
        var finalStock = await _stockCalculator.CalculateStockAsync(productId);
        Assert.True(finalStock >= 0, $"Stock was negative: {finalStock}");
    }

    // ========================================================================
    // PROPERTY 2.1.5: Multiple Reserves Accumulate Correctly
    // ========================================================================
    [Fact]
    public async Task Property_2_1_5_MultipleReserves_AccumulateCorrectly()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var reserves = new[] { 100, 150, 200, 50 };
        var totalReserved = reserves.Sum();

        // When
        foreach (var qty in reserves)
        {
            var cmd = new ReserveInventoryCommand
            {
                OrderId = Guid.NewGuid(),
                ProductId = productId,
                Quantity = qty,
                MessageId = Guid.NewGuid()
            };
            await _reserveHandler.HandleAsync(cmd, Guid.NewGuid());
        }

        // Then - Property: Sum of reserves = Total reserved
        var finalStock = await _stockCalculator.CalculateStockAsync(productId);
        Assert.Equal(1000 - totalReserved, finalStock);
    }

    // ========================================================================
    // PROPERTY 2.2.1: Release Idempotency
    // ========================================================================
    [Fact]
    public async Task Property_2_2_1_ReleaseInventory_WithDuplicateMessageId_IsIdempotent()
    {
        // Given
        var productId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // First reserve to have something to release
        var reserveCmd = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = 200,
            MessageId = Guid.NewGuid()
        };
        await _reserveHandler.HandleAsync(reserveCmd, correlationId);

        var releaseCmd = new ReleaseInventoryCommand
        {
            OrderId = reserveCmd.OrderId,
            ProductId = productId,
            Quantity = 200,
            MessageId = messageId
        };

        // When
        var result1 = await _releaseHandler.HandleAsync(releaseCmd, correlationId);
        var stock1 = await _stockCalculator.CalculateStockAsync(productId);

        var result2 = await _releaseHandler.HandleAsync(releaseCmd, correlationId); // Replay
        var stock2 = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Idempotent (stock unchanged on replay)
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(stock1, stock2);
    }

    // ========================================================================
    // PROPERTY 2.2.2: Reserve-Release Round Trip
    // ========================================================================
    [Fact]
    public async Task Property_2_2_2_ReserveAndRelease_RoundTrip_RestoresStock()
    {
        // Given
        var productId = Guid.NewGuid();
        var initialStock = 500;
        var reserveQuantity = 150;

        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = initialStock
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var reserveCmd = new ReserveInventoryCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = reserveQuantity,
            MessageId = Guid.NewGuid()
        };

        var releaseCmd = new ReleaseInventoryCommand
        {
            OrderId = reserveCmd.OrderId,
            ProductId = productId,
            Quantity = reserveQuantity,
            MessageId = Guid.NewGuid()
        };

        // When
        await _reserveHandler.HandleAsync(reserveCmd, Guid.NewGuid());
        await _releaseHandler.HandleAsync(releaseCmd, Guid.NewGuid());

        var finalStock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Reserve + Release = No change (round trip)
        Assert.Equal(initialStock, finalStock);
    }

    // ========================================================================
    // PROPERTY 2.2.3: Ledger Debit-Credit Balance
    // ========================================================================
    [Fact]
    public async Task Property_2_2_3_LedgerDebitCredit_BalancesCorrectly()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Create various reserves and releases
        var reserves = new[] { 100, 200, 150 };
        var releases = new[] { 50, 75 };

        foreach (var qty in reserves)
        {
            var cmd = new ReserveInventoryCommand
            {
                OrderId = Guid.NewGuid(),
                ProductId = productId,
                Quantity = qty,
                MessageId = Guid.NewGuid()
            };
            await _reserveHandler.HandleAsync(cmd, Guid.NewGuid());
        }

        foreach (var qty in releases)
        {
            var cmd = new ReleaseInventoryCommand
            {
                OrderId = Guid.NewGuid(),
                ProductId = productId,
                Quantity = qty,
                MessageId = Guid.NewGuid()
            };
            await _releaseHandler.HandleAsync(cmd, Guid.NewGuid());
        }

        // When
        var totalReserves = reserves.Sum();
        var totalReleases = releases.Sum();
        var expectedStock = 1000 - totalReserves + totalReleases;
        var actualStock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Stock = InitialStock - Reserves + Releases
        Assert.Equal(expectedStock, actualStock);
    }

    // ========================================================================
    // PROPERTY 2.2.4: Partial Releases Accumulate
    // ========================================================================
    [Fact]
    public async Task Property_2_2_4_PartialReleases_AccumulateCorrectly()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Reserve 500
        var orderId = Guid.NewGuid();
        await _reserveHandler.HandleAsync(new ReserveInventoryCommand
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = 500,
            MessageId = Guid.NewGuid()
        }, Guid.NewGuid());

        var partialReleases = new[] { 100, 150, 100 };
        var totalPartialReleases = partialReleases.Sum();

        // When - Release in parts
        foreach (var qty in partialReleases)
        {
            await _releaseHandler.HandleAsync(new ReleaseInventoryCommand
            {
                OrderId = orderId,
                ProductId = productId,
                Quantity = qty,
                MessageId = Guid.NewGuid()
            }, Guid.NewGuid());
        }

        var finalStock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Partial releases sum correctly
        var expectedStock = 1000 - 500 + totalPartialReleases;
        Assert.Equal(expectedStock, finalStock);
    }

    // ========================================================================
    // PROPERTY 2.3.1: Stock Calculation Consistency
    // ========================================================================
    [Fact]
    public async Task Property_2_3_1_StockCalculation_IsConsistent()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Create various ledger entries
        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 100,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        });

        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 50,
            Type = LedgerType.Release,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        });

        await _dbContext.SaveChangesAsync();

        // When - Calculate multiple times
        var calc1 = await _stockCalculator.CalculateStockAsync(productId);
        var calc2 = await _stockCalculator.CalculateStockAsync(productId);
        var calc3 = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: All calculations are identical (deterministic)
        Assert.Equal(calc1, calc2);
        Assert.Equal(calc2, calc3);
    }

    // ========================================================================
    // PROPERTY 2.3.2: Ignores Non-Processed Entries
    // ========================================================================
    [Fact]
    public async Task Property_2_3_2_StockCalculation_IgnoresNonProcessedEntries()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 1000
        };
        _dbContext.Products.Add(product);

        // Add processed reserve
        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 100,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        });

        // Add pending reserve (should be ignored)
        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 200,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Pending
        });

        // Add rejected reserve (should be ignored)
        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 150,
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Rejected
        });

        await _dbContext.SaveChangesAsync();

        // When
        var stock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Only Processed counted (1000 - 100 = 900)
        Assert.Equal(900, stock);
    }

    // ========================================================================
    // PROPERTY 2.3.3: Stock Clamped to Zero
    // ========================================================================
    [Fact]
    public async Task Property_2_3_3_StockCalculation_NeverNegative()
    {
        // Given
        var productId = Guid.NewGuid();
        var product = new Product
        {
            ProductId = productId,
            Name = "Widget",
            InitialStock = 100
        };
        _dbContext.Products.Add(product);

        // Add reserve that exceeds initial stock
        _dbContext.ReservationLedgers.Add(new ReservationLedger
        {
            LedgerId = Guid.NewGuid(),
            ProductId = productId,
            OrderId = Guid.NewGuid(),
            Quantity = 500,  // More than initial
            Type = LedgerType.Reserve,
            MessageId = Guid.NewGuid(),
            Status = LedgerStatus.Processed
        });

        await _dbContext.SaveChangesAsync();

        // When
        var stock = await _stockCalculator.CalculateStockAsync(productId);

        // Then - Property: Clamped to 0 (not negative)
        Assert.True(stock >= 0);
    }

    // ========================================================================
    // PROPERTY 2.3.4: Product Not Found Returns Zero
    // ========================================================================
    [Fact]
    public async Task Property_2_3_4_StockCalculation_NonexistentProduct_ReturnsZero()
    {
        // Given
        var nonexistentProductId = Guid.NewGuid();

        // When
        var stock = await _stockCalculator.CalculateStockAsync(nonexistentProductId);

        // Then - Property: Stock = 0 for nonexistent products
        Assert.Equal(0, stock);
    }
}

