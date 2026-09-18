using System.Diagnostics;

namespace Observability.TraceContext;

/// <summary>
/// W3C Trace Context standard implementation (https://www.w3.org/TR/trace-context/)
/// 
/// Format: traceparent: version-traceId-parentId-traceFlags
/// Example: traceparent: 00-0af7651916cd43dd8448eb211c80319c-b9c7c989f97918e1-01
/// 
/// Components:
/// - version: 2 hex digits (00 = current version)
/// - traceId: 32 hex digits (128-bit unique trace identifier)
/// - parentId: 16 hex digits (64-bit unique span identifier, 0 = root)
/// - traceFlags: 2 hex digits (bit 0 = sampled, rest reserved)
/// </summary>
public class W3CTraceContext
{
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";
    private const string Version = "00";

    /// <summary>
    /// Parse W3C traceparent header
    /// 
    /// Expected format: version-traceId-parentId-traceFlags
    /// Example: 00-0af7651916cd43dd8448eb211c80319c-b9c7c989f97918e1-01
    /// </summary>
    public static bool TryParse(string? traceparentHeader, out ActivityContext activityContext)
    {
        activityContext = default;

        if (string.IsNullOrWhiteSpace(traceparentHeader))
            return false;

        var parts = traceparentHeader.Split('-');
        if (parts.Length != 4)
            return false;

        // Parse version
        if (!byte.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var version))
            return false;
        if (version != 0)
            return false;

        // Parse traceId
        if (!ActivityTraceId.TryParse(parts[1], out var traceId))
            return false;

        // Parse spanId
        if (!ActivitySpanId.TryParse(parts[2], out var spanId))
            return false;

        // Parse traceFlags
        if (!byte.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var traceFlags))
            return false;

        var isSampled = (traceFlags & 0x01) != 0;
        var traceState = ActivityTraceState.Empty;

        activityContext = new ActivityContext(traceId, spanId, (ActivityTraceFlags)traceFlags, traceState, isRemote: true);
        return true;
    }

    /// <summary>
    /// Create W3C traceparent header from ActivityContext
    /// </summary>
    public static string Create(ActivityContext context)
    {
        var traceId = context.TraceId.ToHexString();
        var spanId = context.SpanId.ToHexString();
        var traceFlags = ((byte)context.TraceFlags).ToString("x2");

        return $"{Version}-{traceId}-{spanId}-{traceFlags}";
    }

    /// <summary>
    /// Create W3C traceparent header from current Activity (if any)
    /// </summary>
    public static string? GetCurrentTraceparent()
    {
        var activity = Activity.Current;
        return activity != null ? Create(activity.Context) : null;
    }
}

/// <summary>
/// Extension methods for W3C Trace Context propagation
/// </summary>
public static class W3CTraceContextExtensions
{
    /// <summary>
    /// Extract W3C trace context from dictionary (e.g., Kafka headers, HTTP headers)
    /// </summary>
    public static ActivityContext ExtractTraceContext(this IDictionary<string, object?> headers)
    {
        if (headers == null)
            return default;

        // Try to get traceparent header
        if (headers.TryGetValue(W3CTraceContext.TraceParentHeader, out var traceparentObj))
        {
            var traceparent = traceparentObj?.ToString();
            if (W3CTraceContext.TryParse(traceparent, out var context))
                return context;
        }

        // No valid trace context found
        return default;
    }

    /// <summary>
    /// Inject W3C trace context into dictionary (e.g., Kafka headers, HTTP headers)
    /// </summary>
    public static void InjectTraceContext(
        this IDictionary<string, object?> headers,
        ActivityContext context)
    {
        if (headers == null)
            return;

        var traceparent = W3CTraceContext.Create(context);
        headers[W3CTraceContext.TraceParentHeader] = traceparent;
    }

    /// <summary>
    /// Create child span from trace context
    /// </summary>
    public static Activity? CreateChild(this ActivityContext context, string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        if (context == default)
            return null;

        var activity = new Activity(operationName);
        activity.SetParentId(context.TraceId, context.SpanId);
        activity.Start();

        return activity;
    }
}
