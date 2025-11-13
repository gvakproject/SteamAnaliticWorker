using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using SteamAnaliticWorker.Models;

namespace SteamAnaliticWorker.Services;

public interface ICollectionOrchestrator
{
    Task CollectAsync(CancellationToken cancellationToken);
}

public class CollectionOrchestrator : ICollectionOrchestrator
{
    private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(1);

    private readonly SteamAnalyticsService _steamService;
    private readonly DataStorageService _storageService;
    private readonly ILogger<CollectionOrchestrator> _logger;

    public CollectionOrchestrator(
        SteamAnalyticsService steamService,
        DataStorageService storageService,
        ILogger<CollectionOrchestrator> logger)
    {
        _steamService = steamService;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task CollectAsync(CancellationToken cancellationToken)
    {
        var items = await _storageService.GetAllItemsAsync(cancellationToken);

        if (items.Count == 0)
        {
            _logger.LogWarning("Нет предметов для обработки");
            return;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessItemAsync(item, cancellationToken);
                await Task.Delay(RequestDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Сбор отменён для предмета {ItemName} (ItemId: {ItemId})", item.Name, item.ItemId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки предмета {ItemName} (ItemId: {ItemId})", item.Name, item.ItemId);
            }
        }

        _logger.LogInformation("Сбор заказов завершён");
    }

    private async Task ProcessItemAsync(Item item, CancellationToken cancellationToken)
    {
        var buyOrders = await _steamService.GetItemOrdersAsync(item, isBuyOrder: true, cancellationToken);
        if (buyOrders.Count > 0)
        {
            await _storageService.SaveBuyOrdersAsync(buyOrders, cancellationToken);
        }

        var sellOrders = await _steamService.GetItemOrdersAsync(item, isBuyOrder: false, cancellationToken);
        if (sellOrders.Count > 0)
        {
            await _storageService.SaveSellOrdersAsync(sellOrders, cancellationToken);
        }
    }
}
