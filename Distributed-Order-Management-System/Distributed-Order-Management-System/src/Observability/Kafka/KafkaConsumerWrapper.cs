using Microsoft.Extensions.Logging;
using System.Text.Json;
using Contracts.Commands;
using Contracts.Events;

namespace Observability.Kafka;

/// <summary>
/// Kafka Consumer Wrapper
/// 
/// Responsibilities:
/// 1. Intercept all message handling
/// 2. Check InboxMessages table for MessageId (deduplication)
/// 3. If found: Mark as processed, skip handler execution (idempotent)
/// 4. If not found: Execute handler, atomically insert into inbox
/// 5. On handler exception: Retry up to N times
/// 6. On max retries: Route to DLQ
/// 
/// Design Pattern: Inbox Pattern (exact-once-like semantics)
/// - Kafka provides at-least-once delivery
/// - Inbox table ensures exact-once application (idempotent)
/// - DLQ captures permanently failed messages
/// 
/// Usage:
///   var result = await _consumer.ConsumeAsync(
///       message,
///       handler,
///       inboxChecker);
/// 
///   if (!result.Success) route_to_dlq(message, result.Error);
/// </summary>
public interface IKafkaConsumerWrapper
{
    /// <summary>
    /// Consume message with inbox deduplication
    /// </summary>
    Task<KafkaConsumeResult> ConsumeAsync<T>(
        T message,
        Func<T, Task> handler,
        IInboxChecker inboxChecker,
        Guid correlationId,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Consume message synchronously (for non-async handlers)
    /// </summary>
    Task<KafkaConsumeResult> ConsumeAsync<T>(
        T message,
        Action<T> handler,
        IInboxChecker inboxChecker,
        Guid correlationId,
        CancellationToken cancellationToken = default) where T : class;
}

/// <summary>
/// Interface for checking and recording messages in inbox
/// </summary>
public interface IInboxChecker
{
    /// <summary>
    /// Check if message already processed
    /// </summary>
    Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as processed atomically with handler result
    /// </summary>
    Task MarkAsProcessedAsync(
        Guid messageId,
        string messageType,
        string payload,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of consume operation
/// </summary>
public record KafkaConsumeResult
{
    public required bool Success { get; init; }
    public required Guid MessageId { get; init; }
    public bool IsIdempotent { get; init; }  // Was already processed
    public int RetryCount { get; init; }
    public string? Error { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Implementation of Kafka consumer wrapper
/// </summary>
public class KafkaConsumerWrapper : IKafkaConsumerWrapper
{
    private readonly ILogger<KafkaConsumerWrapper> _logger;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;
    private readonly Func<object, Task>? _dlqHandler;

    /// <summary>
    /// Configuration for consumer
    /// </summary>
    public class Configuration
    {
        public int MaxRetries { get; init; } = 3;
        public int RetryDelayMs { get; init; } = 100;
    }

    private readonly Configuration _config;

    public KafkaConsumerWrapper(
        ILogger<KafkaConsumerWrapper> logger,
        Configuration? config = null,
        Func<object, Task>? dlqHandler = null)
    {
        _logger = logger;
        _config = config ?? new Configuration();
        _maxRetries = _config.MaxRetries;
        _retryDelayMs = _config.RetryDelayMs;
        _dlqHandler = dlqHandler;
    }

    /// <summary>
    /// Consume async message with inbox deduplication
    /// </summary>
    public async Task<KafkaConsumeResult> ConsumeAsync<T>(
        T message,
        Func<T, Task> handler,
        IInboxChecker inboxChecker,
        Guid correlationId,
        CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var messageId = ExtractMessageId(message);
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message must have MessageId", nameof(message));

        // Step 1: Check if already processed (inbox pattern)
        var isProcessed = await inboxChecker.IsProcessedAsync(messageId, cancellationToken);
        if (isProcessed)
        {
            _logger.LogInformation(
                "Message already processed (idempotent). MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                messageId, correlationId);

            return new KafkaConsumeResult
            {
                Success = true,
                MessageId = messageId,
                IsIdempotent = true,
                RetryCount = 0
            };
        }

        // Step 2: Attempt to process handler with retries
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _maxRetries)
        {
            try
            {
                // Execute handler
                await handler(message);

                // Step 3: Mark as processed in inbox atomically
                var payload = SerializeMessage(message);
                var messageType = typeof(T).Name;

                await inboxChecker.MarkAsProcessedAsync(
                    messageId,
                    messageType,
                    payload,
                    correlationId,
                    cancellationToken);

                _logger.LogInformation(
                    "Message processed successfully. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                    "MessageType: {MessageType}, Attempt: {Attempt}/{MaxRetries}",
                    messageId, correlationId, messageType, attempt + 1, _maxRetries);

                return new KafkaConsumeResult
                {
                    Success = true,
                    MessageId = messageId,
                    IsIdempotent = false,
                    RetryCount = attempt
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                _logger.LogWarning(ex,
                    "Failed to process message. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                    "Attempt: {Attempt}/{MaxRetries}, ErrorMessage: {ErrorMessage}",
                    messageId, correlationId, attempt, _maxRetries, ex.Message);

                if (attempt < _maxRetries)
                {
                    await Task.Delay(_retryDelayMs * attempt, cancellationToken);  // Exponential backoff
                }
            }
        }

        // Step 4: All retries exhausted - route to DLQ
        _logger.LogError(lastException,
            "Message processing failed after {MaxRetries} attempts. MessageId: {MessageId}, " +
            "CorrelationId: {CorrelationId}. Routing to DLQ.",
            _maxRetries, messageId, correlationId);

        if (_dlqHandler != null)
        {
            try
            {
                await _dlqHandler(message);
                _logger.LogInformation(
                    "Message routed to DLQ. MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                    messageId, correlationId);
            }
            catch (Exception dlqEx)
            {
                _logger.LogError(dlqEx,
                    "Failed to route to DLQ. MessageId: {MessageId}, CorrelationId: {CorrelationId}",
                    messageId, correlationId);
            }
        }

        return new KafkaConsumeResult
        {
            Success = false,
            MessageId = messageId,
            IsIdempotent = false,
            RetryCount = _maxRetries,
            Error = lastException?.Message ?? "Max retries exceeded"
        };
    }

    /// <summary>
    /// Consume synchronous message handler
    /// </summary>
    public async Task<KafkaConsumeResult> ConsumeAsync<T>(
        T message,
        Action<T> handler,
        IInboxChecker inboxChecker,
        Guid correlationId,
        CancellationToken cancellationToken = default) where T : class
    {
        // Wrap sync handler in async
        return await ConsumeAsync(
            message,
            m =>
            {
                handler(m);
                return Task.CompletedTask;
            },
            inboxChecker,
            correlationId,
            cancellationToken);
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    /// <summary>
    /// Extract MessageId from message
    /// </summary>
    private Guid ExtractMessageId<T>(T message) where T : class
    {
        return message switch
        {
            BaseEvent evt => evt.MessageId,
            BaseCommand cmd => cmd.MessageId,
            _ => Guid.Empty
        };
    }

    /// <summary>
    /// Serialize message to JSON for inbox storage
    /// </summary>
    private string SerializeMessage<T>(T message) where T : class
    {
        try
        {
            return JsonSerializer.Serialize(message);
        }
        catch
        {
            return message.ToString() ?? "Unable to serialize";
        }
    }
}

/// <summary>
/// Extension methods for consumer
/// </summary>
public static class KafkaConsumerExtensions
{
    /// <summary>
    /// Consume message and throw on failure (strict mode)
    /// </summary>
    public static async Task ConsumeOrThrowAsync<T>(
        this IKafkaConsumerWrapper consumer,
        T message,
        Func<T, Task> handler,
        IInboxChecker inboxChecker,
        Guid correlationId,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = await consumer.ConsumeAsync(message, handler, inboxChecker, correlationId, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Message processing failed after {result.RetryCount} attempts. Error: {result.Error}");
        }
    }
}
