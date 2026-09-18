using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Entities;

namespace Inventory.Service.Handlers;

/// <summary>
/// Handler for ReleaseInventory command (Compensation)
/// 
/// Called when saga needs to reverse a reservation:
/// 1. Payment failed → Release inventory that was reserved
/// 2. Order cancelled → Release inventory
/// 3. Customer refund → Release inventory
/// 
/// Idempotent: Duplicate MessageIds are safely ignored
/// </summary>
public class ReleaseInventoryHandler
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<ReleaseInventoryHandler> _logger;

    public ReleaseInventoryHandler(
        InventoryDbContext dbContext,
        ILogger<ReleaseInventoryHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Handle release inventory command
    /// </summary>
    public async Task<ReleaseInventoryResult> HandleAsync(
        ReleaseInventoryCommand command,
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
                "Release already processed (idempotent). OrderId: {OrderId}, " +
                "ProductId: {ProductId}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                command.OrderId, command.ProductId, command.MessageId, correlationId);

            return new ReleaseInventoryResult
            {
                Success = true,
                OrderId = command.OrderId,
                ProductId = command.ProductId,
                Quantity = command.Quantity,
                Reason = "Already released (idempotent)",
                IsIdempotent = true
            };
        }

        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // Verify reservation exists (for debugging)
                var originalReservation = await _dbContext.ReservationLedgers
                    .Where(rl => rl.OrderId == command.OrderId
                        && rl.ProductId == command.ProductId
                        && rl.Type == LedgerType.Reserve
                        && rl.Status == LedgerStatus.Processed)
                    .FirstOrDefaultAsync();

                if (originalReservation == null)
                {
                    _logger.LogWarning(
                        "No matching reservation found to release. OrderId: {OrderId}, " +
                        "ProductId: {ProductId}, MessageId: {MessageId}",
                        command.OrderId, command.ProductId, command.MessageId);
                    // Still proceed - create release entry for audit trail
                }

                // Create release ledger entry
                var releaseEntry = new ReservationLedger
                {
                    LedgerId = Guid.NewGuid(),
                    ProductId = command.ProductId,
                    OrderId = command.OrderId,
                    Quantity = command.Quantity,
                    Type = LedgerType.Release,
                    MessageId = command.MessageId,
                    Status = LedgerStatus.Processed,
                    CorrelationId = correlationId,
                    ProcessedAt = DateTime.UtcNow
                };

                _dbContext.ReservationLedgers.Add(releaseEntry);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Inventory released (compensation). OrderId: {OrderId}, ProductId: {ProductId}, " +
                    "Quantity: {Quantity}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                    command.OrderId, command.ProductId, command.Quantity,
                    command.MessageId, correlationId);

                return new ReleaseInventoryResult
                {
                    Success = true,
                    OrderId = command.OrderId,
                    ProductId = command.ProductId,
                    Quantity = command.Quantity,
                    Reason = "Inventory released",
                    IsIdempotent = false
                };
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Database error during release. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex,
                    "Unexpected error during release. OrderId: {OrderId}, MessageId: {MessageId}",
                    command.OrderId, command.MessageId);
                throw;
            }
        }
    }

    private void ValidateCommand(ReleaseInventoryCommand command)
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
/// Command for releasing inventory (compensation)
/// </summary>
public record ReleaseInventoryCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required Guid MessageId { get; init; }
}

/// <summary>
/// Result of release operation
/// </summary>
public record ReleaseInventoryResult
{
    public required bool Success { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required string Reason { get; init; }
    public required bool IsIdempotent { get; init; }
}
