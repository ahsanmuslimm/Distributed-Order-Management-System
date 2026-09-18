using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Observability.Instrumentation;

/// <summary>
/// OpenTelemetry configuration for distributed tracing
/// 
/// Configures:
/// - Trace exporter (Jaeger via gRPC)
/// - Span processors (batch processing)
/// - Instrumentation (ASP.NET Core, EF Core, HTTP client)
/// - Resource attributes (service name, version, etc.)
/// </summary>
public static class OpenTelemetryConfiguration
{
    /// <summary>
    /// Add OpenTelemetry tracing to DI container
    /// </summary>
    public static IServiceCollection AddOpenTelemetryTracing(
        this IServiceCollection services,
        string serviceName,
        string? jaegerHost = null,
        int jaegerPort = 14250)
    {
        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["environment"] = GetEnvironment(),
                ["version"] = "1.0.0",
                ["os"] = Environment.OSVersion.Platform.ToString(),
                ["machine"] = Environment.MachineName,
            });

        var jaegerEndpoint = jaegerHost ?? "localhost";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddDetector(new EnvironmentVariableDetector()))
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    // Trace exporters
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri($"http://{jaegerEndpoint}:{jaegerPort}");
                        options.Protocol = OtlpExportProtocol.Grpc;
                        options.Timeout = TimeSpan.FromSeconds(5);
                    })
                    // Auto-instrumentation
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = (request) =>
                        {
                            // Skip health check endpoints
                            return !request.Path.StartsWithSegments("/health");
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.SetDbStatementForText = true;
                    })
                    // Custom instrumentation
                    .AddKafkaInstrumentation()
                    // Manual instrumentation sources
                    .AddSource("Observability.Kafka")
                    .AddSource($"{serviceName}.Handlers")
                    .AddSource($"{serviceName}.Services")
                    // Sampling
                    .SetSampler(new AlwaysOnSampler());

                // Batch processor for efficiency
                tracing.AddBatchSpanProcessor(new BatchSpanProcessorOptions
                {
                    MaxExportBatchSize = 512,
                    ScheduledDelayMilliseconds = 5000,
                });
            });

        return services;
    }

    private static string GetEnvironment()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    }

    /// <summary>
    /// Activity source for manual instrumentation
    /// </summary>
    public static ActivitySource CreateActivitySource(string name, string? version = "1.0.0")
    {
        return new ActivitySource(name, version);
    }
}

/// <summary>
/// Extension to add Kafka instrumentation
/// </summary>
internal static class KafkaInstrumentationExtension
{
    public static TracerProviderBuilder AddKafkaInstrumentation(this TracerProviderBuilder builder)
    {
        return builder.AddSource("Observability.Kafka");
    }
}

/// <summary>
/// Environment variable resource detector
/// </summary>
internal class EnvironmentVariableDetector : IResourceDetector
{
    public Resource Detect()
    {
        var attributes = new Dictionary<string, object>();

        // Add any environment variables prefixed with OTEL_
        foreach (var env in Environment.GetEnvironmentVariables())
        {
            if (env is System.Collections.DictionaryEntry entry &&
                entry.Key is string key &&
                key.StartsWith("OTEL_"))
            {
                attributes[key] = entry.Value?.ToString() ?? "";
            }
        }

        return new Resource(attributes);
    }
}
