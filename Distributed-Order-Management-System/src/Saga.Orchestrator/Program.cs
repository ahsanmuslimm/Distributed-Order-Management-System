using Microsoft.EntityFrameworkCore;
using Saga.Orchestrator.Data;
using Saga.Orchestrator.Handlers;
using Serilog;
using Serilog.Events;

// ========================================================================
// Configure Serilog First
// ========================================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithMachineName()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ========================================================================
    // Logging
    // ========================================================================
    builder.Host.UseSerilog();

    // ========================================================================
    // Database
    // ========================================================================
    var connectionString = builder.Configuration.GetConnectionString("SagaDb")
        ?? "User Id=postgres;Password=postgres;Host=localhost;Port=5436;Database=saga;";

    builder.Services.AddDbContext<SagaDbContext>(options =>
        options.UseNpgsql(connectionString, npg => npg.MigrationsAssembly("Saga.Orchestrator"))
    );

    // ========================================================================
    // Services
    // ========================================================================
    builder.Services.AddScoped<ISagaOrchestrator, SagaOrchestratorHandler>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // ========================================================================
    // Build
    // ========================================================================
    var app = builder.Build();

    // ========================================================================
    // Apply Migrations
    // ========================================================================
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        try
        {
            Log.Information("Applying database migrations...");
            await dbContext.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply database migrations");
            throw;
        }
    }

    // ========================================================================
    // Middleware
    // ========================================================================
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("AllowAll");
    app.UseRouting();

    // ========================================================================
    // Endpoints
    // ========================================================================

    // Health check
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "saga-orchestrator" }))
        .WithName("Health")
        .WithOpenApi();

    // Saga endpoints
    var sagaGroup = app.MapGroup("/api/saga")
        .WithTags("Saga")
        .WithOpenApi();

    sagaGroup.MapPost("/configure-crash", HandleConfigureCrash)
        .WithName("ConfigureCrash")
        .WithOpenApi();

    Log.Information("Saga Orchestrator Service starting on {Url}", app.Urls.FirstOrDefault() ?? "unknown");
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

// ========================================================================
// Handlers
// ========================================================================

async Task<IResult> HandleConfigureCrash()
{
    try
    {
        // For now, just acknowledge the crash configuration
        Log.Information("Crash configuration received");
        return Results.Ok(new { status = "crash configured" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error configuring crash");
        return Results.StatusCode(500);
    }
}

namespace Saga.Orchestrator
{
    // Namespace for Program
}
