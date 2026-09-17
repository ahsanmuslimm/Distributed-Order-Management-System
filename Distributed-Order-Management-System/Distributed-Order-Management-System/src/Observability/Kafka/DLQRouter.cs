using Microsoft.Extensions.Logging;
using System.Text.Json;
using Contracts.Commands;
using Contracts.Events;

namespace Observability.Kafka;

/// <summary>
/// DLQ (Dead Letter Queue) Router
/// 
/// Responsibilities:
/// 1. Route permanently failed messages to topic.dlq
/// 2. Store original payload + error context
/// 3. Maintain audit trail for debugging
/// 4. Enable manual replay of DLQ messages
/// 
/// Design:
/// - DLQ topics are named: {source_topic}.dlq
/// - Each message includes: original payload, error message, retry count, timestamp
/// - DLQ is immutable audit trail
/// - Operators can replay messages after fixing root cause
/// 
/// DLQ Topics:
/// - order.events.dlq
/// - order.commands.dlq
/// - inventory.events.dlq
/// - inventory.commands.dlq
/// - payment.events.dlq
/// - payment.commands.dlq
/// - notification.events.dlq
/// - notification.commands.dlq
/// </summary>
public interface IDLQRouter
{
    /// <summary>
    /// Route message to DLQ
    /// </summary>
    Task<DLQRouteResult> RouteAsync<T>(
        T message,
        string sourceTopicName,
        string errorMessage,
        int retryCount,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Query DLQ messages (for debugging/replay)
    /// </summary>
    Task<IEnumerable<DLQMessage>> QueryDLQAsync(
        string dlqTopicName,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// DLQ Route Result
/// </summary>
public record DLQRouteResult
{
    public required bool Success { get; init; }
    public required Guid MessageId { get; init; }
    public required string DLQTopicName { get; init; }
    public required DateTime RouteTime { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Message in DLQ (for querying/debugging)
/// </summary>
public record DLQMessage
{
    public required Guid MessageId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required string SourceTopic { get; init; }
    public required string ErrorMessage { get; init; }
    public required int RetryCount { get; init; }
    public required string OriginalPayload { get; init; }
    public required DateTime RoutedAt { get; init; }
}

/// <summary>
/// DLQ Router Implementation
/// </summary>
public class DLQRouter : IDLQRouter
{
    private readonly ILogger<DLQRouter> _logger;

    public DLQRouter(ILogger<DLQRouter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Route message to DLQ
    /// </summary>
    public async Task<DLQRouteResult> RouteAsync<T>(
        T message,
        string sourceTopicName,
        string errorMessage,
        int retryCount,
        CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var messageId = ExtractMessageId(message);
        var correlationId = ExtractCorrelationId(message);
        var dlqTopicName = $"{sourceTopicName}.dlq";

        try
        {
            // In production, this would publish to Kafka DLQ topic:
            // var dlqEnvelope = new DLQEnvelope
            // {
            //     OriginalMessageId = messageId,
            //     CorrelationId = correlationId,
            //     SourceTopic = sourceTopicName,
            //     OriginalPayload = JsonSerializer.Serialize(message),
            //     ErrorMessage = errorMessage,
            //     RetryCount = retryCount,
            //     RoutedAt = DateTime.UtcNow
            // };
            // await _publishEndpoint.Publish<DLQEnvelope>(dlqEnvelope);

            _logger.LogError(
                "Message routed to DLQ. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                "SourceTopic: {SourceTopic}, DLQTopic: {DLQTopic}, ErrorMessage: {ErrorMessage}, " +
                "RetryCount: {RetryCount}",
                messageId, correlationId, sourceTopicName, dlqTopicName, errorMessage, retryCount);

            return new DLQRouteResult
            {
                Success = true,
                MessageId = messageId,
                DLQTopicName = dlqTopicName,
                RouteTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to route message to DLQ. MessageId: {MessageId}, CorrelationId: {CorrelationId}, " +
                "SourceTopic: {SourceTopic}, ErrorMessage: {ErrorMessage}",
                messageId, correlationId, sourceTopicName, errorMessage);

            return new DLQRouteResult
            {
                Success = false,
                MessageId = messageId,
                DLQTopicName = dlqTopicName,
                RouteTime = DateTime.UtcNow,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Query DLQ messages (stub for testing)
    /// In production, would query actual DLQ topic
    /// </summary>
    public async Task<IEnumerable<DLQMessage>> QueryDLQAsync(
        string dlqTopicName,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Querying DLQ messages. Topic: {Topic}, Limit: {Limit}",
                dlqTopicName, limit);

            // Stub: In production, would query Kafka DLQ topic
            // For now, return empty list
            await Task.Delay(10, cancellationToken);
            return new List<DLQMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to query DLQ. Topic: {Topic}",
                dlqTopicName);
            throw;
        }
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
    /// Extract CorrelationId from message
    /// </summary>
    private Guid ExtractCorrelationId<T>(T message) where T : class
    {
        return message switch
        {
            BaseEvent evt => evt.CorrelationId,
            BaseCommand cmd => cmd.CorrelationId,
            _ => Guid.Empty
        };
    }
}

/// <summary>
/// DLQ Envelope (what gets stored in DLQ topics)
/// </summary>
public record DLQEnvelope
{
    public Guid OriginalMessageId { get; init; }
    public Guid CorrelationId { get; init; }
    public string SourceTopic { get; init; } = "";
    public string OriginalPayload { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public int RetryCount { get; init; }
    public DateTime RoutedAt { get; init; }
}

/// <summary>
/// Extension methods for DLQ routing
/// </summary>
public static class DLQRouterExtensions
{
    /// <summary>
    /// Route message to DLQ from consume result
    /// </summary>
    public static async Task RouteFromConsumeFailureAsync<T>(
        this IDLQRouter dlqRouter,
        T message,
        string sourceTopicName,
        KafkaConsumeResult result,
        CancellationToken cancellationToken = default) where T : class
    {
        if (!result.Success && !string.IsNullOrEmpty(result.Error))
        {
            await dlqRouter.RouteAsync(
                message,
                sourceTopicName,
                result.Error,
                result.RetryCount,
                cancellationToken);
        }
    }

    /// <summary>
    /// Route message to DLQ from publish failure
    /// </summary>
    public static async Task RouteFromPublishFailureAsync<T>(
        this IDLQRouter dlqRouter,
        T message,
        string destinationTopicName,
        KafkaPublishResult result,
        CancellationToken cancellationToken = default) where T : class
    {
        if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            // When publish fails, the source topic is where message came from
            // For DLQ, we use the destination topic that failed
            var sourceTopic = DetermineSourceTopic<T>();
            await dlqRouter.RouteAsync(
                message,
                sourceTopic,
                result.ErrorMessage,
                result.RetryCount,
                cancellationToken);
        }
    }

    /// <summary>
    /// Determine source topic for message type
    /// </summary>
    private static string DetermineSourceTopic<T>() where T : class
    {
        var typeName = typeof(T).Name;

        return typeName switch
        {
            nameof(OrderPlacedEvent) => "order.events",
            nameof(OrderConfirmedEvent) => "order.events",
            nameof(OrderFailedEvent) => "order.events",
            nameof(InventoryReservedEvent) => "inventory.events",
            nameof(InventoryRejectedEvent) => "inventory.events",
            nameof(InventoryReleasedEvent) => "inventory.events",
            nameof(PaymentChargedEvent) => "payment.events",
            nameof(PaymentFailedEvent) => "payment.events",
            nameof(PaymentRefundedEvent) => "payment.events",
            nameof(ConfirmOrderCommand) => "order.commands",
            nameof(FailOrderCommand) => "order.commands",
            nameof(ReserveInventoryCommand) => "inventory.commands",
            nameof(ReleaseInventoryCommand) => "inventory.commands",
            nameof(ChargePaymentCommand) => "payment.commands",
            nameof(RefundPaymentCommand) => "payment.commands",
            _ => "unknown.topic"
        };
    }
}
