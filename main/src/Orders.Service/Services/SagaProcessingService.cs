using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orders.Service.Data;
using Orders.Service.Entities;
using System.Net.Http.Json;

namespace Orders.Service.Services;

/// <summary>
/// Background service that processes orders through the saga workflow
/// 
/// Process:
/// 1. Poll for pending orders
/// 2. Call Inventory Service to reserve stock
/// 3. Call Payment Service to charge payment
/// 4. Mark order as Confirmed if all successful
/// 5. Handle compensation if any step fails
/// </summary>
public class SagaProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SagaProcessingService> _logger;
    private readonly HttpClient _httpClient;

    public SagaProcessingService(
        IServiceProvider serviceProvider,
        ILogger<SagaProcessingService> logger,
        HttpClient httpClient)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = httpClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Saga Processing Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                    // Find pending orders (status = Pending)
                    var pendingOrders = await dbContext.Orders
                        .Where(o => o.Status == OrderStatus.Pending)
                        .Include(o => o.Items)
                        .ToListAsync(stoppingToken);

                    foreach (var order in pendingOrders)
                    {
                        try
                        {
                            await ProcessOrderSagaAsync(order, dbContext, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing saga for order {OrderId}", order.OrderId);
                            // Continue processing other orders
                        }
                    }
                }

                // Poll every 500ms
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in saga processing loop");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Saga Processing Service stopped");
    }

    /// <summary>
    /// Process order through saga workflow
    /// </summary>
    private async Task ProcessOrderSagaAsync(Order order, OrderDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing saga for order {OrderId}", order.OrderId);

        // Step 1: Reserve inventory
        var inventoryReserved = await TryReserveInventoryAsync(order, cancellationToken);
        if (!inventoryReserved)
        {
            _logger.LogWarning("Inventory reservation failed for order {OrderId}", order.OrderId);
            // Mark as failed
            order.Status = OrderStatus.Failed;
            order.LastUpdatedAt = DateTime.UtcNow;
            dbContext.Orders.Update(order);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        _logger.LogInformation("Inventory reserved for order {OrderId}", order.OrderId);

        // Step 2: Charge payment
        var paymentCharged = await TryChargePaymentAsync(order, cancellationToken);
        if (!paymentCharged)
        {
            _logger.LogWarning("Payment failed for order {OrderId}, compensating inventory", order.OrderId);
            // Compensate: Release inventory
            await TryReleaseInventoryAsync(order, cancellationToken);
            // Mark as failed
            order.Status = OrderStatus.Failed;
            order.LastUpdatedAt = DateTime.UtcNow;
            dbContext.Orders.Update(order);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        _logger.LogInformation("Payment charged for order {OrderId}", order.OrderId);

        // Step 3: Confirm order
        order.Status = OrderStatus.Confirmed;
        order.LastUpdatedAt = DateTime.UtcNow;
        dbContext.Orders.Update(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} confirmed!", order.OrderId);
    }

    /// <summary>
    /// Try to reserve inventory from Inventory Service
    /// </summary>
    private async Task<bool> TryReserveInventoryAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var request = new
            {
                orderId = order.OrderId,
                items = order.Items.Select(i => new
                {
                    productId = i.ProductId,
                    quantity = i.Quantity
                }).ToArray()
            };

            var response = await _httpClient.PostAsJsonAsync(
                "http://localhost:5002/api/inventory/reserve",
                request,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving inventory for order {OrderId}", order.OrderId);
            return false;
        }
    }

    /// <summary>
    /// Try to release inventory (compensation)
    /// </summary>
    private async Task<bool> TryReleaseInventoryAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var request = new
            {
                orderId = order.OrderId,
                items = order.Items.Select(i => new
                {
                    productId = i.ProductId,
                    quantity = i.Quantity
                }).ToArray()
            };

            var response = await _httpClient.PostAsJsonAsync(
                "http://localhost:5002/api/inventory/release",
                request,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing inventory for order {OrderId}", order.OrderId);
            return false;
        }
    }

    /// <summary>
    /// Try to charge payment from Payment Service
    /// </summary>
    private async Task<bool> TryChargePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var request = new
            {
                orderId = order.OrderId,
                customerId = order.CustomerId,
                amount = order.TotalAmount
            };

            var response = await _httpClient.PostAsJsonAsync(
                "http://localhost:5003/api/payments/charge",
                request,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error charging payment for order {OrderId}", order.OrderId);
            return false;
        }
    }
}
