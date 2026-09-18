using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.OpenApi;
using Payment.Service.Data;
using Payment.Service.Domain;
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

// Failure injection for testing (set to 0.0 in production, higher in tests)
builder.Services.AddScoped<IPaymentFailureInjector>(provider =>
    new ConfigurablePaymentFailureInjector(failureRate: 0.0)  // 0% failure in production
);

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

// ========================================================================
// Run
// ========================================================================

app.Run();
