using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Inventory.Service.Infrastructure;

namespace Inventory.Service.Middleware;

/// <summary>
/// Middleware to log all incoming HTTP requests and responses
/// 
/// Logs (as structured JSON):
/// - Method: GET, POST, etc.
/// - Path: /api/catalog, /api/products/{id}/stock
/// - StatusCode: 200, 202, 404, 500
/// - Duration: Time to process request
/// - CorrelationId: For tracing
/// 
/// Example log entry:
/// ```
/// {
///   "EventId": 0,
///   "LogLevel": "Information",
///   "Category": "Inventory.Service.Middleware.RequestLoggingMiddleware",
///   "Message": "Completed HTTP request",
///   "Method": "GET",
///   "Path": "/api/catalog",
///   "StatusCode": 200,
///   "DurationMs": 15,
///   "CorrelationId": "550e8400-e29b-41d4-a716-446655440000"
/// }
/// ```
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = CorrelationIdContext.Get();

        try
        {
            await _next(context);

            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;

            LogRequest(context, durationMs, correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;

            _logger.LogError(ex,
                "HTTP request failed. Method: {Method}, Path: {Path}, " +
                "Duration: {DurationMs}ms, CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                durationMs,
                correlationId);

            throw;
        }
    }

    private void LogRequest(HttpContext context, long durationMs, Guid correlationId)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        var statusCode = context.Response.StatusCode;

        _logger.LogInformation(
            "HTTP request completed. Method: {Method}, Path: {Path}, " +
            "StatusCode: {StatusCode}, Duration: {DurationMs}ms, CorrelationId: {CorrelationId}",
            method,
            path,
            statusCode,
            durationMs,
            correlationId);
    }
}

/// <summary>
/// Extension method to register middleware
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}

