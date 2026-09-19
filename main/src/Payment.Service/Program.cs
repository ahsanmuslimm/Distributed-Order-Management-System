using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.OpenApi;
using Payment.Service.Data;
using Payment.Service.Domain;
using Payment.Service.Endpoints;
using Payment.Service.Handlers;
using Entities = Payment.Service.Entities;

var builder = WebApplication.CreateBuilder(args);

// ========================================================================
// Add Services
// ========================================================================

// Database
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PaymentDb")
        ?? "User Id=postgres;Password=postgres;Host=localhost;Port=5432;Database=payments;",
        npg => npg.MigrationsAssembly("Payment.Service"))
);

// Handlers
builder.Services.AddScoped<ChargePaymentHandler>();
builder.Services.AddScoped<RefundPaymentHandler>();

// Endpoint handlers
builder.Services.AddScoped<ChargePaymentEndpoint>();
builder.Services.AddScoped<RefundPaymentEndpoint>();

// Failure injection for testing (set to 0.0 in production, higher in tests)
// Use a singleton wrapper so we can reconfigure it via admin endpoint
var failureInjectorWrapper = new MutablePaymentFailureInjector();
builder.Services.AddSingleton<IPaymentFailureInjector>(failureInjectorWrapper);
builder.Services.AddSingleton(failureInjectorWrapper);  // Also register the wrapper directly for admin access

// API
builder.Services.AddEndpointsApiExplorer();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ========================================================================
// Build App
// ========================================================================

var app = builder.Build();

// ========================================================================
// Middleware Pipeline
// ========================================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Payment Service API");
    });
}

app.UseCors("AllowAll");

// ========================================================================
// Apply Migrations
// ========================================================================

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await dbContext.Database.MigrateAsync();
}

// ========================================================================
// Endpoints
// ========================================================================

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payment" }))
    .WithName("Health");

// Payment endpoints
ChargePaymentEndpoint.Map(app);
RefundPaymentEndpoint.Map(app);

// ========================================================================
// Admin Endpoints (for testing only)
// ========================================================================

// Configure payment failure for testing
app.MapPost("/admin/payment-failure", async (HttpContext context, MutablePaymentFailureInjector injector) =>
{
    try
    {
        var request = await context.Request.ReadFromJsonAsync<PaymentFailureConfigRequest>();
        if (request == null)
            return Results.BadRequest("Invalid request body");

        injector.SetFailureRate(request.ErrorType switch
        {
            "Temporary" => 0.5,  // 50% temporary failure
            "Permanent" => 1.0,  // 100% permanent failure
            _ => 0.0             // Reset to no failure
        });

        return Results.Ok(new { status = "configured", failureRate = injector.GetFailureRate() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.WithName("ConfigurePaymentFailure")
.WithOpenApi();

// ========================================================================
// Run
// ========================================================================

app.Run();

// ========================================================================
// Helper Classes for Admin Endpoints
// ========================================================================

/// <summary>
/// Mutable payment failure injector that can be reconfigured via admin endpoint
/// </summary>
public class MutablePaymentFailureInjector : IPaymentFailureInjector
{
    private double _failureRate = 0.0;
    private readonly object _lockObj = new object();
    private readonly Random _random = new Random();

    public void SetFailureRate(double rate)
    {
        lock (_lockObj)
        {
            if (rate < 0.0 || rate > 1.0)
                throw new ArgumentException("FailureRate must be between 0.0 and 1.0");
            _failureRate = rate;
        }
    }

    public double GetFailureRate()
    {
        lock (_lockObj)
        {
            return _failureRate;
        }
    }

    public bool ShouldFail()
    {
        lock (_lockObj)
        {
            if (_failureRate == 0.0)
                return false;
            if (_failureRate == 1.0)
                return true;
            return _random.NextDouble() < _failureRate;
        }
    }
}

/// <summary>
/// Request model for payment failure configuration
/// </summary>
public class PaymentFailureConfigRequest
{
    public string? ErrorType { get; set; } // "Temporary", "Permanent", or null to reset
    public bool Enabled { get; set; }
}

