using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Entities;

namespace Inventory.Service.Domain;

/// <summary>
/// Domain service for calculating current stock from immutable ledger
/// 
/// Formula: CurrentStock = InitialStock - sum(Reserves) + sum(Releases)
/// 
/// Why separate service?
/// 1. Single Responsibility: Stock calculation logic isolated
/// 2. Testability: Can test without full HTTP context
/// 3. Reusability: Used by multiple handlers (GetStock endpoint, ReserveInventory handler)
/// 4. Clarity: Business rule is explicit and documented
/// </summary>
public interface IStockCalculator
{
    /// <summary>
    /// Calculate current stock for a product
    /// </summary>
    Task<int> CalculateStockAsync(Guid productId);

    /// <summary>
    /// Check if sufficient stock is available
    /// </summary>
    Task<bool> HasSufficientStockAsync(Guid productId, int requestedQuantity);
}

/// <summary>
/// Implementation of stock calculator
/// </summary>
public class StockCalculator : IStockCalculator
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<StockCalculator> _logger;

    public StockCalculator(InventoryDbContext dbContext, ILogger<StockCalculator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Calculate current stock from ledger
    /// 
    /// Query pattern:
    /// 1. Get product's InitialStock
    /// 2. Sum all Reserve entries (subtract from stock)
    /// 3. Sum all Release entries (add back to stock)
    /// 4. Return: InitialStock - Reserves + Releases
    /// 
    /// Why only count Processed entries?
    /// - Pending: Not yet confirmed, don't affect available stock
    /// - Rejected: Failed validation, don't affect stock
    /// - DeadLettered: Max retries exceeded, don't affect stock
    /// </summary>
    public async Task<int> CalculateStockAsync(Guid productId)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId cannot be empty", nameof(productId));

        try
        {
            // Get product
            var product = await _dbContext.Products.FindAsync(productId);
            if (product == null)
            {
                _logger.LogWarning("Product not found. ProductId: {ProductId}", productId);
                return 0;  // Product doesn't exist
            }

            // Calculate movements from ledger
            var movements = await _dbContext.ReservationLedgers
                .Where(rl => rl.ProductId == productId && rl.Status == LedgerStatus.Processed)
                .GroupBy(rl => rl.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    TotalQuantity = g.Sum(rl => rl.Quantity)
                })
                .ToListAsync();

            // Extract reserves and releases
            var totalReserves = movements
                .Where(m => m.Type == LedgerType.Reserve)
                .Sum(m => m.TotalQuantity);

            var totalReleases = movements
                .Where(m => m.Type == LedgerType.Release)
                .Sum(m => m.TotalQuantity);

            // Calculate current stock
            var currentStock = product.InitialStock - totalReserves + totalReleases;

            _logger.LogInformation(
                "Stock calculated. ProductId: {ProductId}, InitialStock: {InitialStock}, " +
                "Reserves: {Reserves}, Releases: {Releases}, CurrentStock: {CurrentStock}",
                productId, product.InitialStock, totalReserves, totalReleases, currentStock);

            return Math.Max(0, currentStock);  // Never go below zero
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating stock. ProductId: {ProductId}", productId);
            throw;
        }
    }

    /// <summary>
    /// Check if sufficient stock is available for reservation
    /// </summary>
    public async Task<bool> HasSufficientStockAsync(Guid productId, int requestedQuantity)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId cannot be empty", nameof(productId));

        if (requestedQuantity <= 0)
            throw new ArgumentException("Requested quantity must be positive", nameof(requestedQuantity));

        var currentStock = await CalculateStockAsync(productId);
        var hasStock = currentStock >= requestedQuantity;

        _logger.LogInformation(
            "Stock check. ProductId: {ProductId}, Requested: {Requested}, " +
            "Available: {Available}, Result: {Result}",
            productId, requestedQuantity, currentStock, hasStock ? "SUFFICIENT" : "INSUFFICIENT");

        return hasStock;
    }
}
