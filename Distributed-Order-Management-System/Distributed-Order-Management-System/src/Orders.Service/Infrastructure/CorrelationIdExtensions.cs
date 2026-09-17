using Microsoft.AspNetCore.Http;

namespace Orders.Service.Infrastructure;

/// <summary>
/// Extension methods for Correlation ID handling
/// </summary>
public static class CorrelationIdExtensions
{
    /// <summary>
    /// Header name for Correlation ID
    /// </summary>
    public const string CorrelationIdHeader = "Correlation-ID";

    /// <summary>
    /// Extract Correlation ID from HTTP request header
    /// If missing, generate a new one
    /// </summary>
    public static Guid ExtractOrGenerateCorrelationId(this HttpRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Try to extract from header
        if (request.Headers.TryGetValue(CorrelationIdHeader, out var headerValue))
        {
            if (Guid.TryParse(headerValue.ToString(), out var correlationId) && correlationId != Guid.Empty)
            {
                return correlationId;
            }
        }

        // Generate new if not present or invalid
        return Guid.NewGuid();
    }

    /// <summary>
    /// Add Correlation ID to HTTP response header
    /// </summary>
    public static void AddCorrelationIdHeader(this HttpResponse response, Guid correlationId)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));

        response.Headers.Add(CorrelationIdHeader, correlationId.ToString());
    }
}
