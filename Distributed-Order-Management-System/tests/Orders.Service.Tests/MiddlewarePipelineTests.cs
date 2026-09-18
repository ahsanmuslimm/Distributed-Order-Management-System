using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orders.Service.Infrastructure;
using Orders.Service.Middleware;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Integration tests for middleware pipeline
/// Tests end-to-end request flow with CorrelationId
/// </summary>
public class MiddlewarePipelineTests
{
    // ========================================================================
    // Test: Middleware Extracts and Propagates Correlation ID
    // ========================================================================
    [Fact]
    public async Task Middleware_Extracts_CorrelationIdFromHeader()
    {
        // Arrange
        var correlationId = Guid.NewGuid();

        var app = new WebApplicationBuilder()
            .ConfigureServices(services => services.AddLogging())
            .Build();

        app.UseCorrelationId();

        app.Run(context =>
        {
            // Assert: CorrelationId should be available in context
            var current = CorrelationIdContext.Current;
            Assert.Equal(correlationId, current);

            // Assert: Should be in response header
            Assert.True(
                context.Response.Headers.TryGetValue(
                    CorrelationIdExtensions.CorrelationIdHeader,
                    out var responseValue));
            Assert.Equal(correlationId.ToString(), responseValue.ToString());

            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var client = new TestClient(app);

        // Act
        var response = await client.GetAsync("/", new Dictionary<string, string>
        {
            { CorrelationIdExtensions.CorrelationIdHeader, correlationId.ToString() }
        });

        // Assert
        Assert.True(response.StatusCode == StatusCodes.Status200OK);
        Assert.True(
            response.Headers.TryGetValues(
                CorrelationIdExtensions.CorrelationIdHeader,
                out var values));
        Assert.Contains(correlationId.ToString(), values);
    }

    // ========================================================================
    // Test: Middleware Generates Correlation ID if Missing
    // ========================================================================
    [Fact]
    public async Task Middleware_Generates_CorrelationIdIfMissing()
    {
        // Arrange
        Guid generatedCorrelationId = Guid.Empty;

        var app = new WebApplicationBuilder()
            .ConfigureServices(services => services.AddLogging())
            .Build();

        app.UseCorrelationId();

        app.Run(context =>
        {
            generatedCorrelationId = CorrelationIdContext.Current;
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var client = new TestClient(app);

        // Act
        var response = await client.GetAsync("/", new Dictionary<string, string>());

        // Assert
        Assert.NotEqual(Guid.Empty, generatedCorrelationId);
        Assert.True(response.StatusCode == StatusCodes.Status200OK);
        Assert.True(
            response.Headers.TryGetValues(
                CorrelationIdExtensions.CorrelationIdHeader,
                out var values));
        Assert.Contains(generatedCorrelationId.ToString(), values);
    }

    // ========================================================================
    // Test: Correlation ID Cleared After Request
    // ========================================================================
    [Fact]
    public async Task Middleware_Clears_CorrelationIdAfterRequest()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        Guid beforeId = Guid.Empty, afterId = Guid.Empty;

        var app = new WebApplicationBuilder()
            .ConfigureServices(services => services.AddLogging())
            .Build();

        app.UseCorrelationId();

        app.Run(context =>
        {
            beforeId = CorrelationIdContext.Current;
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var client = new TestClient(app);

        // Act
        await client.GetAsync("/", new Dictionary<string, string>
        {
            { CorrelationIdExtensions.CorrelationIdHeader, correlationId.ToString() }
        });

        afterId = CorrelationIdContext.Current;

        // Assert
        Assert.Equal(correlationId, beforeId);
        Assert.Equal(Guid.Empty, afterId);  // Cleared after request
    }

    // ========================================================================
    // Test: Multiple Requests Have Different Correlation IDs
    // ========================================================================
    [Fact]
    public async Task Middleware_DifferentRequests_HaveDifferentCorrelationIds()
    {
        // Arrange
        var ids = new List<Guid>();

        var app = new WebApplicationBuilder()
            .ConfigureServices(services => services.AddLogging())
            .Build();

        app.UseCorrelationId();

        app.Run(context =>
        {
            ids.Add(CorrelationIdContext.Current);
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var client = new TestClient(app);

        // Act
        await client.GetAsync("/");
        await client.GetAsync("/");
        await client.GetAsync("/");

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.NotEqual(ids[0], ids[1]);
        Assert.NotEqual(ids[1], ids[2]);
        Assert.NotEqual(ids[0], ids[2]);
    }
}

/// <summary>
/// Test helper for making HTTP requests
/// </summary>
internal class TestClient
{
    private readonly WebApplication _app;

    public TestClient(WebApplication app)
    {
        _app = app;
    }

    public async Task<TestResponse> GetAsync(
        string path,
        Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();

        // Add headers
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                context.Request.Headers[key] = value;
            }
        }

        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;

        // Create test server and invoke
        var testServer = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                foreach (var service in _app.Services.GetServices(typeof(object)))
                {
                    if (service != null)
                    {
                        // Copy services (simplified)
                    }
                }
            })
            .Configure(builder =>
            {
                builder.UseCorrelationId();
                builder.Run(ctx =>
                {
                    return _app.RequestDelegate!(ctx);
                });
            }));

        using var client = testServer.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.Add(key, value);
            }
        }

        var response = await client.SendAsync(request);

        return new TestResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = response.Headers.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray())
        };
    }
}

/// <summary>
/// Test response
/// </summary>
internal record TestResponse
{
    public int StatusCode { get; set; }
    public Dictionary<string, string[]> Headers { get; set; } = new();
}
