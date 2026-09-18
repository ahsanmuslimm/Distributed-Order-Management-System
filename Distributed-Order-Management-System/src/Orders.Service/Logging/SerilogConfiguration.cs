using Serilog;
using Serilog.Core;
using Serilog.Events;
using Orders.Service.Infrastructure;

namespace Orders.Service.Logging;

/// <summary>
/// Configuration for Serilog structured logging
/// 
/// Features:
/// - JSON output for log aggregation (ELK, Splunk, etc.)
/// - Console output for development
/// - Correlation ID enrichment on all logs
/// - Request/Response logging
/// - Exception details with stack traces
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Configure Serilog for the application
    /// </summary>
    public static LoggerConfiguration ConfigureLogger(
        this LoggerConfiguration config,
        IHostEnvironment environment)
    {
        // Base configuration
        config
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.With<CorrelationIdEnricher>();

        // Console output (always)
        config.WriteTo.Console(
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{MachineName}:{ThreadId}] {CorrelationId} {Message:lj}{NewLine}{Exception}");

        // File output (structured JSON)
        config.WriteTo.File(
            path: "logs/orders-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        // JSON file output (for log aggregation)
        config.WriteTo.File(
            formatter: new Serilog.Formatting.Json.JsonFormatter(),
            path: "logs/orders-json-.txt",
            rollingInterval: RollingInterval.Day);

        // Development-specific
        if (environment.IsDevelopment())
        {
            config.MinimumLevel.Debug();
            config.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information);
        }

        return config;
    }

    /// <summary>
    /// Add Serilog to WebApplicationBuilder
    /// </summary>
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, config) =>
        {
            config.ConfigureLogger(context.HostingEnvironment);
        });

        return builder;
    }
}

/// <summary>
/// Custom Serilog enricher to add Correlation ID to all log events
/// </summary>
public class CorrelationIdEnricher : ILogEventEnricher
{
    /// <summary>
    /// Enrich log event with Correlation ID
    /// </summary>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = CorrelationIdContext.Current;

        if (correlationId != Guid.Empty)
        {
            var property = propertyFactory.CreateProperty("CorrelationId", correlationId);
            logEvent.AddPropertyIfAbsent(property);
        }
        else
        {
            // Default value if not set
            var property = propertyFactory.CreateProperty("CorrelationId", "N/A");
            logEvent.AddPropertyIfAbsent(property);
        }
    }
}
