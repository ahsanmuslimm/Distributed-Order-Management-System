using Microsoft.EntityFrameworkCore;
using Orders.Service.Data;
using Orders.Service.Endpoints;
using Orders.Service.Handlers;
using Orders.Service.Infrastructure;
using Orders.Service.Logging;
using Orders.Service.Middleware;
using Serilog;

var builder = WebApplicationBuilder.CreateBuilder(args);

// ========================================================================
// Configure Logging (Serilog) - MUST be first
// ========================================================================
builder.AddSerilog();

// ========================================================================
// Add Services
// ========================================================================

// Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("OrderDb") 
        ?? "User Id=postgres;Password=postgres;Host=localhost;Port=5432;Database=orders;")
);

// Handlers
builder.Services.AddScoped<PlaceOrderHandler>();
builder.Services.AddScoped<GetOrderStatusHandler>();

// Infrastructure
builder.Services.AddScoped<IInboxProcessor, InboxProcessor>();

// API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// ========================================================================
// Build App
// ========================================================================

var app = builder.Build();

// ========================================================================
// Middleware Pipeline (Order Matters!)
// ========================================================================

// 1. Correlation ID Middleware (MUST be early)
app.UseCorrelationId();

// 2. Request/Response Logging
app.UseRequestLogging();

// 3. Exception handling
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}

// 4. Swagger (dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 5. HTTPS redirect
app.UseHttpsRedirection();

// 6. CORS
app.UseCors("AllowAll");

// 7. Routing & authorization
app.UseRouting();
app.UseAuthorization();

// ========================================================================
// Map Endpoints
// ========================================================================

app.MapControllers();

// Minimal API endpoints
PlaceOrderEndpoint.Map(app);
GetOrderStatusEndpoint.Map(app);

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("Health")
    .WithOpenApi();

// ========================================================================
// Run
// ========================================================================

app.Run();

namespace Orders.Service
{
    // Namespace for Program
}
