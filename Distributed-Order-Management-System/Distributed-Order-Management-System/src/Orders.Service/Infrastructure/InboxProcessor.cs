using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orders.Service.Data;
using Orders.Service.Entities;

namespace Orders.Service.Infrastructure;

/// <summary>
/// Interface for inbox pattern processing
/// Enables idempotent message consumption
/// </summary>
public interface IInboxProcessor
{
    /// <summary>
    /// Check if a message has already been processed
    /// </summary>
    Task<bool> HasProcessedAsync(Guid messageId);

    /// <summary>
    /// Record that a message has been processed
    /// Must be called atomically with message processing
    /// </summary>
    Task<void> RecordProcessedAsync(
        Guid messageId,
        Guid orderId,
        string messageType,
        object payload,
        Guid correlationId);
}

/// <summary>
/// Implementation of Inbox Pattern
/// 
/// Prevents duplicate message processing in the face of at-least-once delivery.
/// 
/// Flow:
/// 1. Message arrives from Kafka
/// 2. Check: HasProcessedAsync(messageId)?
///    - Yes → Already processed, skip
///    - No → Continue to step 3
/// 3. Process message (create order, transition status, etc.)
/// 4. Atomically: RecordProcessedAsync(messageId)
/// 5. Commit transaction
/// 
/// This ensures exactly-once semantics despite at-least-once delivery.
/// </summary>
public class InboxProcessor : IInboxProcessor
{
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<InboxProcessor> _logger;

    public InboxProcessor(OrderDbContext dbContext, ILogger<InboxProcessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    public async Task<bool> HasProcessedAsync(Guid messageId)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty", nameof(messageId));

        try
        {
            var exists = await _dbContext.InboxMessages
                .AsNoTracking()
                .AnyAsync(im => im.MessageId == messageId);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking inbox for MessageId: {MessageId}", messageId);
            throw;
        }
    }

    /// <summary>
    /// Record that a message has been processed
    /// 
    /// IMPORTANT: This MUST be called in the SAME TRANSACTION as the message processing.
    /// Otherwise, if the process crashes between message handling and inbox recording,
    /// the message will be processed again on retry.
    /// 
    /// Typical usage:
    /// using (var transaction = await db.Database.BeginTransactionAsync())
    /// {
    ///     // Process message
    ///     await handler.Handle(message);
    ///     
    ///     // Record in inbox (same transaction)
    ///     await inboxProcessor.RecordProcessedAsync(...);
    ///     
    ///     await transaction.CommitAsync();
    /// }
    /// </summary>
    public async Task<void> RecordProcessedAsync(
        Guid messageId,
        Guid orderId,
        string messageType,
        object payload,
        Guid correlationId)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty", nameof(messageId));

        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId cannot be empty", nameof(orderId));

        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("MessageType cannot be empty", nameof(messageType));

        try
        {
            var inboxMessage = new InboxMessage
            {
                MessageId = messageId,
                OrderId = orderId,
                MessageType = messageType,
                Payload = JsonSerializer.Serialize(payload),
                ProcessedAt = DateTime.UtcNow,
                CorrelationId = correlationId
            };

            _dbContext.InboxMessages.Add(inboxMessage);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Message recorded in inbox. MessageId: {MessageId}, OrderId: {OrderId}, " +
                "Type: {MessageType}, CorrelationId: {CorrelationId}",
                messageId, orderId, messageType, correlationId);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate") ?? false)
        {
            // Duplicate key exception: Message already processed
            // This is expected if the message was processed concurrently
            _logger.LogWarning(
                "Message already in inbox (concurrent processing). MessageId: {MessageId}, OrderId: {OrderId}",
                messageId, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error recording message in inbox. MessageId: {MessageId}, OrderId: {OrderId}",
                messageId, orderId);
            throw;
        }
    }
}
