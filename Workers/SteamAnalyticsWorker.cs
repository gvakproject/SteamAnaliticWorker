using SteamAnaliticWorker.Models;
using SteamAnaliticWorker.Services;

namespace SteamAnaliticWorker.Workers;

public class SteamAnalyticsWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SteamAnalyticsWorker> _logger;
    private readonly TimeSpan _period = TimeSpan.FromHours(1);

    public SteamAnalyticsWorker(
        IServiceProvider serviceProvider,
        ILogger<SteamAnalyticsWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Steam Analytics Worker started. Will run every hour.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAnalyticsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while collecting analytics");
            }

            await Task.Delay(_period, stoppingToken);
        }
    }

    private async Task CollectAnalyticsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting analytics collection at {Time}", DateTime.UtcNow);

        using var scope = _serviceProvider.CreateScope();
        var steamService = scope.ServiceProvider.GetRequiredService<SteamAnalyticsService>();
        var storageService = scope.ServiceProvider.GetRequiredService<DataStorageService>();

        var items = await storageService.GetAllItemsAsync(cancellationToken);

        if (items.Count == 0)
        {
            _logger.LogWarning("No items configured. Please add items to track.");
            return;
        }

        foreach (var item in items)
        {
            try
            {
                // Собираем заказы на покупку
                var buyOrders = await steamService.GetItemOrdersAsync(item, isBuyOrder: true, cancellationToken);
                if (buyOrders.Count > 0)
                {
                    await storageService.SaveBuyOrdersAsync(buyOrders, cancellationToken);
                }

                // Собираем заказы на продажу
                var sellOrders = await steamService.GetItemOrdersAsync(item, isBuyOrder: false, cancellationToken);
                if (sellOrders.Count > 0)
                {
                    await storageService.SaveSellOrdersAsync(sellOrders, cancellationToken);
                }

                // Небольшая задержка между запросами, чтобы не перегружать API
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing item {ItemName} (ItemId: {ItemId})", item.Name, item.ItemId);
            }
        }

        _logger.LogInformation("Analytics collection completed at {Time}", DateTime.UtcNow);
    }
}

