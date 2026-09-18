using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Serilog;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Endpoints;
using Inventory.Service.Handlers;
using Inventory.Service.Infrastructure;
using Inventory.Service.Logging;
using Inventory.Service.Middleware;
using Inventory.Service.Services;

// ========================================================================
// PHASE 2: INVENTORY SERVICE
// ========================================================================
// This service manages inventory (stock) for the distributed order system.
//
// Responsibilities:
// - ReserveInventory: Lock stock when order is placed
// - ReleaseInventory: Unlock stock when order fails or is cancelled
// - GetCatalog: List all products (with Redis cache)
// - GetStock: Get current stock for a product (real-time from ledger)
//
// Key Patterns:
// - Immutable ledger: All inventory movements are append-only
// - Idempotency: Duplicate MessageIds are safely ignored (Inbox Pattern)
// - Redis cache-aside: Catalog cached for 60s, stock always from ledger
// - Structured logging: JSON format for log aggregation
// - Correlation ID: End-to-end tracing across services
// ========================================================================

// Configure Serilog
SerilogConfiguration.ConfigureSerilog();

try
{
    var builder = WebApplicationBuilder.CreateBuilder(args);

    // ====================================================================
    // SERVICES
    // ====================================================================

    // Logging
    builder.Host.UseSerilog();

    // Database
    var connectionString = builder.Configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException("Connection string 'Inventory' not found");

    builder.Services.AddDbContext<InventoryDbContext>(options =>
    {
        options.UseNpgsql(connectionString, npg =>
        {
            npg.MigrationsAssembly("Inventory.Service");
        });
    });

    // Redis
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379";

    try
    {
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddScoped<ICatalogCache, RedisCatalogCache>();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to connect to Redis, using no-op cache");
        builder.Services.AddScoped<ICatalogCache, NoCatalogCache>();
    }

    // Domain services
    builder.Services.AddScoped<IStockCalculator, StockCalculator>();

    // Command handlers
    builder.Services.AddScoped<ReserveInventoryHandler>();
    builder.Services.AddScoped<ReleaseInventoryHandler>();

    // Background services
    builder.Services.AddHostedService<CacheConsistencyJob>();

    // API
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    // ====================================================================
    // BUILD
    // ====================================================================

    var app = builder.Build();

    // Apply migrations
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.MigrateAsync();
        Log.Information("Database migrations applied");
    }

    // ====================================================================
    // MIDDLEWARE PIPELINE
    // ====================================================================
    // Order is CRITICAL:
    // 1. CorrelationId: Extract/set before all others
    // 2. RequestLogging: Log all requests with timing
    // 3. ExceptionHandling: Catch and log errors
    // 4. Swagger/OpenAPI: API documentation
    // 5. Routing: Map endpoints
    // ====================================================================

    app.UseCorrelationId();
    app.UseRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Inventory Service API");
        });
    }

    // ====================================================================
    // ENDPOINTS
    // ====================================================================

    // Catalog (with Redis cache)
    GetCatalogEndpoint.Map(app);

    // Stock (real-time from ledger, no cache)
    GetStockEndpoint.Map(app);

    // Health check
    app.MapGet("/health", async (InventoryDbContext db) =>
    {
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1");
            return Results.Ok(new { status = "healthy", service = "inventory" });
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    });

    Log.Information("Inventory Service starting on {Url}", app.Urls.FirstOrDefault() ?? "unknown");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

