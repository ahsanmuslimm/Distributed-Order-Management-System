using Serilog;
using Serilog.Core;
using Serilog.Events;
using Inventory.Service.Infrastructure;

namespace Inventory.Service.Logging;

/// <summary>
/// Serilog configuration for structured JSON logging
/// 
/// Configuration:
/// - JSON format (suitable for log aggregation)
/// - Console output (development)
/// - File output (production)
/// - Automatic CorrelationId enrichment
/// - Structured properties for querying
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Configure Serilog for the application
    /// </summary>
    public static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.With<CorrelationIdEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}

/// <summary>
/// Custom enricher to automatically add CorrelationId to all log entries
/// </summary>
public class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = CorrelationIdContext.Get();
        
        var property = propertyFactory.CreateProperty(
            "CorrelationId",
            correlationId);

        logEvent.AddPropertyIfAbsent(property);
    }
}

