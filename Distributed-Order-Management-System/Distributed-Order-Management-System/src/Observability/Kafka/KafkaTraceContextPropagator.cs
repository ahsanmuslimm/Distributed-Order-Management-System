using System.Diagnostics;
using Confluent.Kafka;
using Observability.TraceContext;

namespace Observability.Kafka;

/// <summary>
/// Propagates W3C Trace Context through Kafka message headers
/// 
/// Ensures that traces initiated at API Gateway flow through all services:
/// 1. API Gateway → Order Service (HTTP headers)
/// 2. Order Service → Kafka (message headers)
/// 3. Inventory/Payment/Notification ← Kafka (message headers)
/// 4. All services report spans to Jaeger with same trace ID
/// 
/// Result: Single trace spans all services
/// </summary>
public static class KafkaTraceContextPropagator
{
    /// <summary>
    /// Inject W3C trace context into Kafka message headers
    /// 
    /// Called before publishing to Kafka
    /// </summary>
    public static void InjectTraceContext<TKey, TValue>(
        this Message<TKey, TValue> message,
        ActivityContext? context = null)
    {
        if (message == null)
            return;

        message.Headers ??= new Headers();

        var traceContext = context ?? Activity.Current?.Context ?? default;
        if (traceContext == default)
        {
            // Generate new trace if none exists
            using var activity = new Activity("KafkaProducer").Start();
            if (activity != null)
                traceContext = activity.Context;
        }

        var traceparent = W3CTraceContext.Create(traceContext);
        message.Headers.Add(W3CTraceContext.TraceParentHeader, System.Text.Encoding.UTF8.GetBytes(traceparent));

        // Also inject correlation ID if available
        if (traceContext.TraceId != default)
        {
            var correlationId = traceContext.TraceId.ToHexString();
            message.Headers.Add("correlation-id", System.Text.Encoding.UTF8.GetBytes(correlationId));
        }
    }

    /// <summary>
    /// Extract W3C trace context from Kafka message headers
    /// 
    /// Called after consuming from Kafka
    /// </summary>
    public static ActivityContext? ExtractTraceContext(this Headers? headers)
    {
        if (headers == null)
            return null;

        // Look for traceparent header
        var traceparentBytes = headers.FirstOrDefault(h =>
            h.Key.Equals(W3CTraceContext.TraceParentHeader, StringComparison.OrdinalIgnoreCase))?.Value;

        if (traceparentBytes == null)
            return null;

        var traceparent = System.Text.Encoding.UTF8.GetString(traceparentBytes);

        if (W3CTraceContext.TryParse(traceparent, out var context))
            return context;

        return null;
    }

    /// <summary>
    /// Link current activity to extracted trace context
    /// 
    /// Used when processing consumed message
    /// </summary>
    public static Activity? LinkToTraceContext(
        this Headers? headers,
        string operationName,
        ActivityKind kind = ActivityKind.Consumer)
    {
        var context = headers.ExtractTraceContext();
        if (context == null)
            return null;

        var activity = new Activity(operationName)
            .SetParentId(context.Value.TraceId, context.Value.SpanId)
            .SetKind(kind);

        activity.Start();
        return activity;
    }
}

/// <summary>
/// Kafka instrumentation for manual span tracking
/// </summary>
public class KafkaInstrumentationSource : IDisposable
{
    public static readonly ActivitySource Source = new ActivitySource(
        "Observability.Kafka",
        "1.0.0");

    private const string ProducerSpanName = "kafka.producer.send";
    private const string ConsumerSpanName = "kafka.consumer.receive";

    /// <summary>
    /// Record Kafka producer operation
    /// </summary>
    public static Activity? RecordProducerOperation(
        string topic,
        int partitionCount,
        long messageSize)
    {
        var activity = Source.StartActivity(ProducerSpanName, ActivityKind.Producer);
        if (activity != null)
        {
            activity.SetTag("messaging.system", "kafka");
            activity.SetTag("messaging.destination", topic);
            activity.SetTag("messaging.message_id", Guid.NewGuid().ToString());
            activity.SetTag("messaging.kafka.partition_count", partitionCount);
            activity.SetTag("messaging.message_payload_size_bytes", messageSize);
        }

        return activity;
    }

    /// <summary>
    /// Record Kafka consumer operation
    /// </summary>
    public static Activity? RecordConsumerOperation(
        string topic,
        int partition,
        long offset,
        long messageSize)
    {
        var activity = Source.StartActivity(ConsumerSpanName, ActivityKind.Consumer);
        if (activity != null)
        {
            activity.SetTag("messaging.system", "kafka");
            activity.SetTag("messaging.source", topic);
            activity.SetTag("messaging.kafka.partition", partition);
            activity.SetTag("messaging.kafka.offset", offset);
            activity.SetTag("messaging.message_payload_size_bytes", messageSize);
        }

        return activity;
    }

    public void Dispose()
    {
        Source?.Dispose();
    }
}
