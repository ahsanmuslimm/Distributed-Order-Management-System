using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orders.Service.Infrastructure;

namespace Orders.Service.Middleware;

/// <summary>
/// HTTP middleware for Correlation ID extraction and propagation
/// 
/// Flow:
/// 1. Extract Correlation-ID from request header (or generate new)
/// 2. Store in AsyncLocal context (available throughout request)
/// 3. Add to response header (for client tracing)
/// 
/// This enables end-to-end tracing across all services.
/// Every log entry will include the CorrelationId.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Process HTTP request
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate Correlation ID
        var correlationId = context.Request.ExtractOrGenerateCorrelationId();

        // Store in context for this request
        CorrelationIdContext.Set(correlationId);

        try
        {
            // Add to request header (for downstream services)
            context.Request.Headers[CorrelationIdExtensions.CorrelationIdHeader] = correlationId.ToString();

            // Add to response header (for client)
            context.Response.AddCorrelationIdHeader(correlationId);

            _logger.LogInformation(
                "Request started. Path: {Path}, Method: {Method}, CorrelationId: {CorrelationId}",
                context.Request.Path,
                context.Request.Method,
                correlationId);

            // Call next middleware
            await _next(context);

            _logger.LogInformation(
                "Request completed. Status: {StatusCode}, CorrelationId: {CorrelationId}",
                context.Response.StatusCode,
                correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Request failed. Path: {Path}, Method: {Method}, CorrelationId: {CorrelationId}",
                context.Request.Path,
                context.Request.Method,
                correlationId);

            throw;
        }
        finally
        {
            // Clear context at end of request
            CorrelationIdContext.Clear();
        }
    }
}

/// <summary>
/// Extension methods for registering CorrelationIdMiddleware
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Add CorrelationIdMiddleware to the pipeline
    /// Must be registered EARLY in the pipeline (before other middleware)
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
