using Microsoft.EntityFrameworkCore;
using SteamAnaliticWorker.Data;
using SteamAnaliticWorker.Models;

namespace SteamAnaliticWorker.Services;

public class DataStorageService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<DataStorageService> _logger;

    public DataStorageService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<DataStorageService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<List<Item>> GetAllItemsAsync(CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Items.ToListAsync(cancellationToken);
    }

    public async Task<Item?> GetItemByItemIdAsync(string itemId, CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Items.FirstOrDefaultAsync(i => i.ItemId == itemId, cancellationToken);
    }

    public async Task<Item> AddOrUpdateItemAsync(Item item, CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var existingItem = await context.Items.FirstOrDefaultAsync(i => i.ItemId == item.ItemId, cancellationToken);
        
        if (existingItem != null)
        {
            existingItem.Name = item.Name;
            existingItem.LastUpdated = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return existingItem;
        }
        else
        {
            item.LastUpdated = DateTime.UtcNow;
            context.Items.Add(item);
            await context.SaveChangesAsync(cancellationToken);
            return item;
        }
    }

    public async Task SaveOrdersAsync(List<Order> orders, CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0) return;

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        // Все заказы в списке относятся к одному типу (buy/sell)
        var isBuyOrderBatch = orders.First().IsBuyOrder;
        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();

        // Удаляем только дубликаты за текущий час (чтобы не накапливать одинаковые данные)
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);
        
        // Удаляем заказы за текущий час для этих предметов (чтобы обновить данные)
        var duplicateOrders = await context.Orders
            .Where(o => itemIds.Contains(o.ItemId) && 
                       o.IsBuyOrder == isBuyOrderBatch &&
                       o.CollectedAt >= hourStart && 
                       o.CollectedAt < hourEnd)
            .ToListAsync(cancellationToken);
        
        if (duplicateOrders.Count > 0)
        {
            context.Orders.RemoveRange(duplicateOrders);
        }
        
        // Удаляем очень старые данные (старше 30 дней) для экономии места
        var oldCutoff = DateTime.UtcNow.AddDays(-30);
        var veryOldOrders = await context.Orders
            .Where(o => o.CollectedAt < oldCutoff)
            .ToListAsync(cancellationToken);
        
        if (veryOldOrders.Count > 0)
        {
            context.Orders.RemoveRange(veryOldOrders);
            _logger.LogInformation("Removed {Count} old orders (older than 30 days)", veryOldOrders.Count);
        }
        
        // Добавляем новые заказы
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Saved {Count} orders to database", orders.Count);
    }

    public async Task<List<Order>> GetLatestOrdersAsync(int itemId, bool isBuyOrder, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Orders
            .Where(o => o.ItemId == itemId && o.IsBuyOrder == isBuyOrder)
            .OrderByDescending(o => o.CollectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, object>> GetAnalyticsSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var totalItems = await context.Items.CountAsync(cancellationToken);
        var totalBuyOrders = await context.Orders.CountAsync(o => o.IsBuyOrder, cancellationToken);
        var totalSellOrders = await context.Orders.CountAsync(o => !o.IsBuyOrder, cancellationToken);
        var lastUpdate = await context.Items
            .OrderByDescending(i => i.LastUpdated)
            .Select(i => i.LastUpdated)
            .FirstOrDefaultAsync(cancellationToken);

        return new Dictionary<string, object>
        {
            ["totalItems"] = totalItems,
            ["totalBuyOrders"] = totalBuyOrders,
            ["totalSellOrders"] = totalSellOrders,
            ["lastUpdate"] = lastUpdate
        };
    }

    public async Task<List<object>> GetOrdersByTimeGroupingAsync(
        int itemId, 
        bool isBuyOrder, 
        string grouping = "hour",
        int days = 7,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            var cutoffDate = DateTime.UtcNow.AddDays(-days);
            
            var orders = await context.Orders
                .Where(o => o.ItemId == itemId && 
                           o.IsBuyOrder == isBuyOrder && 
                           o.CollectedAt >= cutoffDate)
                .OrderBy(o => o.CollectedAt)
                .ToListAsync(cancellationToken);

            if (orders.Count == 0)
            {
                return new List<object>();
            }

            var grouped = grouping.ToLower() switch
            {
                "hour" => orders.GroupBy(o => new DateTime(
                    o.CollectedAt.Year, 
                    o.CollectedAt.Month, 
                    o.CollectedAt.Day, 
                    o.CollectedAt.Hour, 
                    0, 0, DateTimeKind.Utc)),
                "day" => orders.GroupBy(o => new DateTime(
                    o.CollectedAt.Year, 
                    o.CollectedAt.Month, 
                    o.CollectedAt.Day, 
                    0, 0, 0, DateTimeKind.Utc)),
                _ => orders.GroupBy(o => new DateTime(
                    o.CollectedAt.Year, 
                    o.CollectedAt.Month, 
                    o.CollectedAt.Day, 
                    o.CollectedAt.Hour, 
                    0, 0, DateTimeKind.Utc))
            };

            return grouped.Select(g => new
            {
                time = g.Key,
                avgPrice = g.Average(o => o.Price),
                minPrice = g.Min(o => o.Price),
                maxPrice = g.Max(o => o.Price),
                totalQuantity = g.Sum(o => o.Quantity),
                orderCount = g.Count()
            }).Cast<object>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders by time grouping for item {ItemId}", itemId);
            return new List<object>();
        }
    }

    public async Task<List<object>> GetPriceHistoryAsync(
        int itemId,
        bool isBuyOrder,
        int days = 7,
        CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        
        var orders = await context.Orders
            .Where(o => o.ItemId == itemId && 
                       o.IsBuyOrder == isBuyOrder && 
                       o.CollectedAt >= cutoffDate)
            .OrderBy(o => o.CollectedAt)
            .Select(o => new { o.Price, o.Quantity, o.CollectedAt })
            .ToListAsync(cancellationToken);

        return orders.Cast<object>().ToList();
    }
}

