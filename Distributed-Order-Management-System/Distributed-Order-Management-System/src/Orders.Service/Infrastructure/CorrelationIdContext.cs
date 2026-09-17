namespace Orders.Service.Infrastructure;

/// <summary>
/// Ambient context for Correlation ID
/// 
/// Uses AsyncLocal to store correlation ID so it's available
/// throughout the request lifecycle without passing it explicitly.
/// 
/// Correlation ID enables end-to-end tracing across all services.
/// </summary>
public static class CorrelationIdContext
{
    private static readonly AsyncLocal<Guid> CorrelationIdStorage = new();

    /// <summary>
    /// Get the current Correlation ID (or empty if not set)
    /// </summary>
    public static Guid Current => CorrelationIdStorage.Value == Guid.Empty 
        ? Guid.Empty 
        : CorrelationIdStorage.Value;

    /// <summary>
    /// Set the Correlation ID for this request
    /// </summary>
    public static void Set(Guid correlationId)
    {
        CorrelationIdStorage.Value = correlationId;
    }

    /// <summary>
    /// Clear the Correlation ID
    /// </summary>
    public static void Clear()
    {
        CorrelationIdStorage.Value = Guid.Empty;
    }

    /// <summary>
    /// Use Correlation ID for a scope (IDisposable pattern)
    /// Useful in tests or manual contexts where we want to ensure cleanup
    /// </summary>
    public static IDisposable Use(Guid correlationId)
    {
        var previous = CorrelationIdStorage.Value;
        CorrelationIdStorage.Value = correlationId;
        
        return new CorrelationIdScope(previous);
    }

    /// <summary>
    /// Internal scope class for disposing
    /// </summary>
    private class CorrelationIdScope : IDisposable
    {
        private readonly Guid _previousValue;

        public CorrelationIdScope(Guid previousValue)
        {
            _previousValue = previousValue;
        }

        public void Dispose()
        {
            CorrelationIdStorage.Value = _previousValue;
        }
    }
}
