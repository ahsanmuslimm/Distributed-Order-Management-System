using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Entities;

namespace Inventory.Service.Handlers;

/// <summary>
/// Handler for ReserveInventory command
/// 
/// Idempotent message handler that:
/// 1. Checks if message already processed (Inbox Pattern)
/// 2. Calculates current stock
/// 3. If sufficient: Create Reserve ledger entry → Publish InventoryReservedEvent
/// 4. If insufficient: Publish InventoryRejectedEvent
/// 5. Record in inbox (atomic with ledger update)
/// </summary>
public class ReserveInventoryHandler
{
    private readonly InventoryDbContext _dbContext;
    private readonly IStockCalculator _stockCalculator;
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(
        InventoryDbContext dbContext,
        IStockCalculator stockCalculator,
        ILogger<ReserveInventoryHandler> logger)
    {
        _dbContext = dbContext;
        _stockCalculator = stockCalculator;
        _logger = logger;
    }

    /// <summary>
    /// Handle reserve inventory command
    /// </summary>
    public async Task<ReserveInventoryResult> HandleAsync(
        ReserveInventoryCommand command,
        Guid correlationId)
    {
        ValidateCommand(command);

        // Check if already processed (Inbox Pattern)
        var alreadyProcessed = await _dbContext.ReservationLedgers
            .AsNoTracking()
            .AnyAsync(rl => rl.MessageId == command.MessageId);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Reserve already processed (idempotent). OrderId: {OrderId}, " +
                "ProductId: {ProductId}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                command.OrderId, command.ProductId, command.MessageId, correlationId);

            return new ReserveInventoryResult
            {
                Success = true,
                OrderId = command.OrderId,
                ProductId = command.ProductId,
                Quantity = command.Quantity,
                Reason = "Already reserved (idempotent)",
                IsIdempotent = true
            };
        }

        // Check stock
        var hasSufficientStock = await _stockCalculator.HasSufficientStockAsync(
            command.ProductId,
            command.Quantity);

        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                if (hasSufficientStock)
                {
                    // Create reserve ledger entry
                    var ledgerEntry = new ReservationLedger
                    {
                        LedgerId = Guid.NewGuid(),
                        ProductId = command.ProductId,
                        OrderId = command.OrderId,
                        Quantity = command.Quantity,
                        Type = LedgerType.Reserve,
                        MessageId = command.MessageId,
                        Status = LedgerStatus.Processed,
                        CorrelationId = correlationId
                    };

                    _dbContext.ReservationLedgers.Add(ledgerEntry);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "Inventory reserved. OrderId: {OrderId}, ProductId: {ProductId}, " +
                        "Quantity: {Quantity}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                        command.OrderId, command.ProductId, command.Quantity, 
                        command.MessageId, correlationId);

                    return new ReserveInventoryResult
                    {
                        Success = true,
                        OrderId = command.OrderId,
                        ProductId = command.ProductId,
                        Quantity = command.Quantity,
                        Reason = "Inventory reserved",
                        IsIdempotent = false
                    };
                }
                else
                {
                    // Create reject ledger entry for audit
                    var currentStock = await _stockCalculator.CalculateStockAsync(command.ProductId);
                    var ledgerEntry = new ReservationLedger
                    {
                        LedgerId = Guid.NewGuid(),
                        ProductId = command.ProductId,
                        OrderId = command.OrderId,
                        Quantity = command.Quantity,
                        Type = LedgerType.Reserve,
                        MessageId = command.MessageId,
                        Status = LedgerStatus.Rejected,
                        CorrelationId = correlationId
                    };

                    _dbContext.ReservationLedgers.Add(ledgerEntry);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogWarning(
                        "Inventory reservation rejected (insufficient stock). OrderId: {OrderId}, " +
                        "ProductId: {ProductId}, Requested: {Requested}, Available: {Available}, " +
                        "MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                        command.OrderId, command.ProductId, command.Quantity, 
                        currentStock, command.MessageId, correlationId);

                    return new ReserveInventoryResult
                    {
                        Success = false,
                        OrderId = command.OrderId,
                        ProductId = command.ProductId,
                        Quantity = command.Quantity,
                        Reason = $"Insufficient stock. Available: {currentStock}",
                        IsIdempotent = false
                    };
                }
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error during reserve. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error during reserve. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
        }
    }

    private void ValidateCommand(ReserveInventoryCommand command)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (command.OrderId == Guid.Empty)
            throw new ArgumentException("OrderId cannot be empty", nameof(command.OrderId));

        if (command.ProductId == Guid.Empty)
            throw new ArgumentException("ProductId cannot be empty", nameof(command.ProductId));

        if (command.Quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(command.Quantity));

        if (command.MessageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty", nameof(command.MessageId));
    }
}

/// <summary>
/// Command for reserving inventory
/// </summary>
public record ReserveInventoryCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required Guid MessageId { get; init; }
}

/// <summary>
/// Result of reserve operation
/// </summary>
public record ReserveInventoryResult
{
    public required bool Success { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required string Reason { get; init; }
    public required bool IsIdempotent { get; init; }
}
