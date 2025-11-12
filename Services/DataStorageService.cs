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
        
        // Удаляем старые заказы для этих предметов (старше 1 часа)
        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();
        var cutoffTime = DateTime.UtcNow.AddHours(-1);
        
        var oldOrders = await context.Orders
            .Where(o => itemIds.Contains(o.ItemId) && o.CollectedAt < cutoffTime)
            .ToListAsync(cancellationToken);
        
        if (oldOrders.Count > 0)
        {
            context.Orders.RemoveRange(oldOrders);
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
}

