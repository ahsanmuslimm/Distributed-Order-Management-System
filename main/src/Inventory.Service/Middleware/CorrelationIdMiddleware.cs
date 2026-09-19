using Inventory.Service.Infrastructure;

namespace Inventory.Service.Middleware;

/// <summary>
/// ASP.NET Core middleware for correlation ID propagation
/// 
/// Must be registered FIRST in middleware pipeline (before all others)
/// 
/// Flow:
/// 1. Extract CorrelationId from X-Correlation-ID header (or generate)
/// 2. Set in AsyncLocal context (CorrelationIdContext)
/// 3. Add to response header (for client to track)
/// 4. Call next middleware
/// 5. Clear from context (cleanup)
/// 
/// Why first?
/// - All subsequent middleware can access via CorrelationIdContext.Get()
/// - Logging middleware uses it for enrichment
/// - Database handlers use it for audit trail
/// - Message handlers use it for end-to-end tracing
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate Correlation ID
        var correlationId = context.Request.ExtractOrGenerateCorrelationId();

        // Set in async context
        CorrelationIdContext.Set(correlationId);

        try
        {
            // Add to response header
            context.Response.AddCorrelationIdHeader(correlationId);

            // Call next middleware
            await _next(context);
        }
        finally
        {
            // Cleanup
            CorrelationIdContext.Clear();
        }
    }
}

/// <summary>
/// Extension method to register middleware
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}

