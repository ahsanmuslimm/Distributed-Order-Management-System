using Serilog;
using Serilog.Formatting.Compact;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Data;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(
        "logs/payment-service-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

// Add services
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
