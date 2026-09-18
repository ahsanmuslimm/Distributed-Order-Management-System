using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Contracts.Commands;
using Contracts.Events;

namespace Observability.Kafka;

/// <summary>
/// Kafka Producer Wrapper
/// 
/// Responsibilities:
/// 1. Inject MessageId into message headers (or generate if missing)
/// 2. Inject CorrelationId into headers for distributed tracing
/// 3. Publish to Kafka topic (determined by message type)
/// 4. Wait for broker ACK (In-Sync Replicas = 1)
/// 5. Implement retry logic on transient failures
/// 6. On max retries, route to DLQ
/// 
/// Design:
/// - Decorates MassTransit IPublishEndpoint
/// - Enforces header injection before send
/// - Provides metrics on publish success/failure
/// 
/// Usage:
///   var result = await _kafkaProducer.PublishAsync(orderPlacedEvent);
///   if (!result.Success) route_to_dlq(orderPlacedEvent);
/// </summary>
public interface IKafkaProducerWrapper
{
    /// <summary>
    /// Publish event with automatic MessageId/CorrelationId injection
    /// </summary>
    Task<KafkaPublishResult> PublishAsync<T>(
        T message,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Publish command
    /// </summary>
    Task<KafkaPublishResult> PublishCommandAsync<T>(
        T command,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default) where T : BaseCommand;
}

/// <summary>
/// Result of publish operation
/// </summary>
public record KafkaPublishResult
{
    public required bool Success { get; init; }
    public required Guid MessageId { get; init; }
    public required string TopicName { get; init; }
    public int RetryCount { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime PublishedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Implementation of Kafka producer wrapper
/// Uses MassTransit for publish operations
/// </summary>
public class KafkaProducerWrapper : IKafkaProducerWrapper
{
    private readonly ILogger<KafkaProducerWrapper> _logger;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    /// <summary>
    /// Configuration for producer
    /// </summary>
    public class Configuration
    {
        public int MaxRetries { get; init; } = 3;
        public int RetryDelayMs { get; init; } = 100;
        public int AckTimeoutMs { get; init; } = 5000;
    }

    private readonly Configuration _config;

    public KafkaProducerWrapper(
        ILogger<KafkaProducerWrapper> logger,
        Configuration? config = null)
    {
        _logger = logger;
        _config = config ?? new Configuration();
        _maxRetries = _config.MaxRetries;
        _retryDelayMs = _config.RetryDelayMs;
    }

    /// <summary>
    /// Publish event with retry logic
    /// </summary>
    public async Task<KafkaPublishResult> PublishAsync<T>(
        T message,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        // Extract or generate MessageId
        var messageId = ExtractMessageId(message) ?? Guid.NewGuid();
        var corrId = correlationId ?? ExtractCorrelationId(message) ?? Guid.NewGuid();
        var topicName = DetermineTopicName(message);

        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _maxRetries)
        {
            try
            {
                // Attempt publish
                await PublishToKafkaAsync(message, messageId, corrId, topicName, cancellationToken);

                _logger.LogInformation(
                    "Message published successfully. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                    "Topic: {Topic}, Attempt: {Attempt}/{MaxRetries}",
                    messageId, corrId, topicName, attempt + 1, _maxRetries);

                return new KafkaPublishResult
                {
                    Success = true,
                    MessageId = messageId,
                    TopicName = topicName,
                    RetryCount = attempt
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                _logger.LogWarning(ex,
                    "Failed to publish message. MessageId: {MessageId}, Attempt: {Attempt}/{MaxRetries}, " +
                    "Topic: {Topic}, ErrorMessage: {ErrorMessage}",
                    messageId, attempt, _maxRetries, topicName, ex.Message);

                if (attempt < _maxRetries)
                {
                    await Task.Delay(_retryDelayMs * attempt, cancellationToken);  // Exponential backoff
                }
            }
        }

        // All retries exhausted
        _logger.LogError(lastException,
            "Message publishing failed after {MaxRetries} attempts. MessageId: {MessageId}, " +
            "CorrelationId: {CorrelationId}, Topic: {Topic}",
            _maxRetries, messageId, corrId, topicName);

        return new KafkaPublishResult
        {
            Success = false,
            MessageId = messageId,
            TopicName = topicName,
            RetryCount = _maxRetries,
            ErrorMessage = lastException?.Message ?? "Max retries exceeded"
        };
    }

    /// <summary>
    /// Publish command (same as PublishAsync, just typed for clarity)
    /// </summary>
    public async Task<KafkaPublishResult> PublishCommandAsync<T>(
        T command,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default) where T : BaseCommand
    {
        return await PublishAsync(command, correlationId, cancellationToken);
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    /// <summary>
    /// Actual Kafka publish operation
    /// In real implementation, would use MassTransit IPublishEndpoint
    /// For now, this is a stub that demonstrates the pattern
    /// </summary>
    private async Task PublishToKafkaAsync<T>(
        T message,
        Guid messageId,
        Guid correlationId,
        string topicName,
        CancellationToken cancellationToken) where T : class
    {
        // In production, this would be:
        // var headers = new Confluent.Kafka.Headers
        // {
        //     { "MessageId", Encoding.UTF8.GetBytes(messageId.ToString()) },
        //     { "CorrelationId", Encoding.UTF8.GetBytes(correlationId.ToString()) }
        // };
        //
        // // Inject W3C trace context
        // var activity = Activity.Current ?? new Activity("KafkaProducer").Start();
        // var traceparent = W3CTraceContext.Create(activity.Context);
        // headers.Add("traceparent", Encoding.UTF8.GetBytes(traceparent));
        //
        // await _publishEndpoint.Publish(message, context =>
        // {
        //     context.SetDelayedRedelivery(TimeSpan.FromSeconds(1));
        // }, cancellationToken);

        // Record Kafka instrumentation
        using (var activity = KafkaInstrumentationSource.RecordProducerOperation(
            topicName,
            partitionCount: 1,
            messageSize: JsonSerializer.Serialize(message).Length))
        {
            // For testing, simulate async operation
            await Task.Delay(10, cancellationToken);

            _logger.LogDebug(
                "Message prepared for Kafka. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                "Topic: {Topic}, TraceId: {TraceId}, SpanId: {SpanId}",
                messageId, correlationId, topicName,
                Activity.Current?.Id, Activity.Current?.SpanId);
        }
    }

    /// <summary>
    /// Extract MessageId from message if it's BaseEvent or BaseCommand
    /// </summary>
    private Guid? ExtractMessageId<T>(T message) where T : class
    {
        return message switch
        {
            BaseEvent evt => evt.MessageId,
            BaseCommand cmd => cmd.MessageId,
            _ => null
        };
    }

    /// <summary>
    /// Extract CorrelationId from message if it's BaseEvent or BaseCommand
    /// </summary>
    private Guid? ExtractCorrelationId<T>(T message) where T : class
    {
        return message switch
        {
            BaseEvent evt => evt.CorrelationId,
            BaseCommand cmd => cmd.CorrelationId,
            _ => null
        };
    }

    /// <summary>
    /// Determine Kafka topic name based on message type
    /// </summary>
    private string DetermineTopicName<T>(T message) where T : class
    {
        return message switch
        {
            // Order events
            OrderPlacedEvent => "order.events",
            OrderConfirmedEvent => "order.events",
            OrderFailedEvent => "order.events",

            // Inventory events
            InventoryReservedEvent => "inventory.events",
            InventoryRejectedEvent => "inventory.events",
            InventoryReleasedEvent => "inventory.events",

            // Payment events
            PaymentChargedEvent => "payment.events",
            PaymentFailedEvent => "payment.events",
            PaymentRefundedEvent => "payment.events",

            // Order commands (from orchestrator)
            ConfirmOrderCommand => "order.commands",
            FailOrderCommand => "order.commands",

            // Inventory commands (from orchestrator)
            ReserveInventoryCommand => "inventory.commands",
            ReleaseInventoryCommand => "inventory.commands",

            // Payment commands (from orchestrator)
            ChargePaymentCommand => "payment.commands",
            RefundPaymentCommand => "payment.commands",

            _ => throw new ArgumentException($"Unknown message type: {typeof(T).Name}")
        };
    }
}

/// <summary>
/// Extension methods for producer
/// </summary>
public static class KafkaProducerExtensions
{
    /// <summary>
    /// Publish or route to DLQ on failure
    /// </summary>
    public static async Task PublishOrDLQAsync<T>(
        this IKafkaProducerWrapper producer,
        T message,
        Func<T, Task> dlqHandler,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = await producer.PublishAsync(message, correlationId, cancellationToken);

        if (!result.Success)
        {
            await dlqHandler(message);
        }
    }
}
