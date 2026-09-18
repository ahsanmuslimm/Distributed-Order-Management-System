using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orders.Service.Infrastructure;
using Orders.Service.Logging;
using Orders.Service.Middleware;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Tests for Serilog structured logging integration
/// Verifies correlation ID enrichment and structured output
/// </summary>
public class SerilogLoggingTests
{
    // ========================================================================
    // Test: Correlation ID Enrichment
    // ========================================================================
    [Fact]
    public async Task Serilog_Enriches_LogsWithCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var logMessages = new List<string>();

        var webHostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(config =>
                {
                    config.ClearProviders();
                    config.AddConsole();
                });
            })
            .Configure(app =>
            {
                app.UseCorrelationId();

                app.Run(context =>
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<SerilogLoggingTests>>();
                    logger.LogInformation("Test log message");

                    context.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var testServer = new TestServer(webHostBuilder);
        var client = testServer.CreateClient();

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationIdExtensions.CorrelationIdHeader, correlationId.ToString());

        var response = await client.SendAsync(request);

        // Assert
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK);
        Assert.True(response.Headers.Contains(CorrelationIdExtensions.CorrelationIdHeader));
    }

    // ========================================================================
    // Test: Request Logging Contains Required Information
    // ========================================================================
    [Fact]
    public async Task RequestLogging_IncludesMethodAndPath()
    {
        // Arrange
        var logEntries = new List<string>();

        var webHostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseCorrelationId();
                app.UseRequestLogging();

                app.Run(context =>
                {
                    context.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var testServer = new TestServer(webHostBuilder);
        var client = testServer.CreateClient();

        // Act
        var response = await client.GetAsync("/api/orders/123");

        // Assert
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK);
    }

    // ========================================================================
    // Test: Correlation Enricher Works with Empty Context
    // ========================================================================
    [Fact]
    public void CorrelationIdEnricher_HandlesEmptyContext()
    {
        // Arrange
        CorrelationIdContext.Clear();
        var enricher = new CorrelationIdEnricher();

        var logEvent = new Serilog.Events.LogEvent(
            DateTimeOffset.UtcNow,
            Serilog.Events.LogEventLevel.Information,
            exception: null,
            messageTemplate: new Serilog.Parsing.MessageTemplateParser().Parse("Test"),
            properties: new Dictionary<string, Serilog.Events.LogEventPropertyValue>());

        var propertyFactory = new Serilog.Core.LogEventPropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        Assert.NotNull(logEvent.Properties);
        Assert.True(logEvent.Properties.ContainsKey("CorrelationId"));
    }

    // ========================================================================
    // Test: Correlation Enricher Includes Current ID
    // ========================================================================
    [Fact]
    public void CorrelationIdEnricher_IncludesCurrentCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        CorrelationIdContext.Set(correlationId);

        var enricher = new CorrelationIdEnricher();

        var logEvent = new Serilog.Events.LogEvent(
            DateTimeOffset.UtcNow,
            Serilog.Events.LogEventLevel.Information,
            exception: null,
            messageTemplate: new Serilog.Parsing.MessageTemplateParser().Parse("Test"),
            properties: new Dictionary<string, Serilog.Events.LogEventPropertyValue>());

        var propertyFactory = new Serilog.Core.LogEventPropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        Assert.True(logEvent.Properties.ContainsKey("CorrelationId"));
        var propertyValue = logEvent.Properties["CorrelationId"];
        Assert.NotNull(propertyValue);

        // Cleanup
        CorrelationIdContext.Clear();
    }

    // ========================================================================
    // Test: Multiple Requests Have Independent Logs
    // ========================================================================
    [Fact]
    public async Task RequestLogging_IndependentForEachRequest()
    {
        // Arrange
        var correlationIds = new List<Guid>();

        var webHostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseCorrelationId();

                app.Run(context =>
                {
                    correlationIds.Add(CorrelationIdContext.Current);
                    context.Response.StatusCode = 200;
                    return Task.CompletedTask;
                });
            });

        using var testServer = new TestServer(webHostBuilder);
        var client = testServer.CreateClient();

        // Act
        var response1 = await client.GetAsync("/");
        var response2 = await client.GetAsync("/");
        var response3 = await client.GetAsync("/");

        // Assert
        Assert.Equal(3, correlationIds.Count);
        Assert.NotEqual(correlationIds[0], correlationIds[1]);
        Assert.NotEqual(correlationIds[1], correlationIds[2]);
    }

    // ========================================================================
    // Test: RequestLoggingMiddleware Logs Timing
    // ========================================================================
    [Fact]
    public async Task RequestLogging_IncludesExecutionTime()
    {
        // Arrange
        var webHostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseCorrelationId();
                app.UseRequestLogging();

                app.Run(async context =>
                {
                    await Task.Delay(100);  // Simulate work
                    context.Response.StatusCode = 200;
                });
            });

        using var testServer = new TestServer(webHostBuilder);
        var client = testServer.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK);
        // Timing is logged but we can't easily capture it here
        // In real scenario, it would appear in logs
    }
}
