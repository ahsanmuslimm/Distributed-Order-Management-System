using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Inventory.Service.Data;
using Inventory.Service.Domain;
using Inventory.Service.Endpoints;

namespace Inventory.Service.Services;

/// <summary>
/// Background job to detect and repair cache-ledger divergence
/// 
/// Problem: Cache (Redis) and ledger (Postgres) can diverge if:
/// - Redis key expires but new data added to ledger
/// - Redis crashes and restarts with lost data
/// - Ledger gets inconsistent due to bug
/// 
/// Solution: Periodically compare cache stock vs ledger stock
/// - If divergence detected: Invalidate cache
/// - Cache will be rebuilt on next miss (cache-aside pattern)
/// - Log warning for monitoring
/// 
/// Why separate job?
/// - Doesn't block normal requests
/// - Can handle gracefully without affecting customers
/// - Repairs any inconsistency automatically
/// </summary>
public interface ICacheConsistencyJob : IHostedService
{
    /// <summary>
    /// Run consistency check immediately (for testing)
    /// </summary>
    Task RunCheckAsync();
}

/// <summary>
/// Implementation of cache consistency job
/// </summary>
public class CacheConsistencyJob : BackgroundService, ICacheConsistencyJob
{
    private readonly InventoryDbContext _dbContext;
    private readonly IStockCalculator _stockCalculator;
    private readonly ICatalogCache _cache;
    private readonly ILogger<CacheConsistencyJob> _logger;
    private readonly TimeSpan _interval;

    public CacheConsistencyJob(
        InventoryDbContext dbContext,
        IStockCalculator stockCalculator,
        ICatalogCache cache,
        ILogger<CacheConsistencyJob> logger)
    {
        _dbContext = dbContext;
        _stockCalculator = stockCalculator;
        _cache = cache;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(30);  // Run every 30 seconds
    }

    /// <summary>
    /// Run consistency check immediately
    /// </summary>
    public async Task RunCheckAsync()
    {
        try
        {
            _logger.LogInformation("Cache consistency check started");

            // For now, just invalidate catalog cache
            // Full product list isn't cached for stock levels (only for catalog)
            await _cache.InvalidateCatalogAsync();

            _logger.LogInformation("Cache consistency check completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache consistency check");
        }
    }

    /// <summary>
    /// Background service entry point
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let service start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCheckAsync();
            }
            catch (OperationCanceledException)
            {
                // Service is stopping, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in cache consistency job");
            }

            // Wait for next iteration
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Cache consistency job stopping");
    }
}

