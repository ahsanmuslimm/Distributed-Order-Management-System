namespace Inventory.Service.Infrastructure;

/// <summary>
/// Context for managing correlation ID throughout async call chain
/// 
/// Problem: Correlation ID needs to flow from HTTP request through
/// async message processing, but HttpContext.TraceIdentifier only works
/// within a single HTTP request scope.
/// 
/// Solution: Store in AsyncLocal so it flows naturally through async/await
/// 
/// Example:
/// 1. HTTP middleware extracts CorrelationId from header (or generates new)
/// 2. Sets via CorrelationIdContext.Set(id)
/// 3. Message handler reads via CorrelationIdContext.Get()
/// 4. Automatically propagates through async chains
/// </summary>
public static class CorrelationIdContext
{
    private static readonly AsyncLocal<Guid> _correlationId = new();

    /// <summary>
    /// Set correlation ID for current async context
    /// </summary>
    public static void Set(Guid id)
    {
        _correlationId.Value = id;
    }

    /// <summary>
    /// Get correlation ID for current async context
    /// </summary>
    public static Guid Get()
    {
        var id = _correlationId.Value;
        return id != Guid.Empty ? id : Guid.NewGuid();
    }

    /// <summary>
    /// Clear correlation ID (cleanup)
    /// </summary>
    public static void Clear()
    {
        _correlationId.Value = Guid.Empty;
    }
}

/// <summary>
/// Extension methods for correlation ID handling
/// </summary>
public static class CorrelationIdExtensions
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

    /// <summary>
    /// Extract correlation ID from HTTP request headers or generate new
    /// </summary>
    public static Guid ExtractOrGenerateCorrelationId(this HttpRequest request)
    {
        if (request.Headers.TryGetValue(CorrelationIdHeader, out var value))
        {
            if (Guid.TryParse(value.ToString(), out var correlationId))
            {
                return correlationId;
            }
        }

        return Guid.NewGuid();
    }

    /// <summary>
    /// Add correlation ID to HTTP response headers
    /// </summary>
    public static void AddCorrelationIdHeader(this HttpResponse response, Guid correlationId)
    {
        if (!response.Headers.ContainsKey(CorrelationIdHeader))
        {
            response.Headers.Add(CorrelationIdHeader, correlationId.ToString());
        }
    }
}

