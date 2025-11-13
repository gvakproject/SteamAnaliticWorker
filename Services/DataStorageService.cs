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

    public async Task SaveBuyOrdersAsync(List<Order> orders, CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0) return;

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();

        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);

        var duplicateOrders = await context.BuyOrders
            .Where(o => itemIds.Contains(o.ItemId) &&
                        o.CollectedAt >= hourStart &&
                        o.CollectedAt < hourEnd)
            .ToListAsync(cancellationToken);

        if (duplicateOrders.Count > 0)
        {
            context.BuyOrders.RemoveRange(duplicateOrders);
        }

        var oldCutoff = DateTime.UtcNow.AddDays(-30);
        var veryOldOrders = await context.BuyOrders
            .Where(o => o.CollectedAt < oldCutoff)
            .ToListAsync(cancellationToken);

        if (veryOldOrders.Count > 0)
        {
            context.BuyOrders.RemoveRange(veryOldOrders);
            _logger.LogInformation("Removed {Count} old buy orders (older than 30 days)", veryOldOrders.Count);
        }

        var buyRecords = orders.Select(o => new BuyOrderRecord
        {
            ItemId = o.ItemId,
            Price = o.Price,
            Quantity = o.Quantity,
            CollectedAt = o.CollectedAt
        });

        context.BuyOrders.AddRange(buyRecords);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved {Count} buy orders to database", orders.Count);
    }

    public async Task SaveSellOrdersAsync(List<Order> orders, CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0) return;

        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();

        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);

        var duplicateOrders = await context.SellOrders
            .Where(o => itemIds.Contains(o.ItemId) &&
                        o.CollectedAt >= hourStart &&
                        o.CollectedAt < hourEnd)
            .ToListAsync(cancellationToken);

        if (duplicateOrders.Count > 0)
        {
            context.SellOrders.RemoveRange(duplicateOrders);
        }

        var oldCutoff = DateTime.UtcNow.AddDays(-30);
        var veryOldOrders = await context.SellOrders
            .Where(o => o.CollectedAt < oldCutoff)
            .ToListAsync(cancellationToken);

        if (veryOldOrders.Count > 0)
        {
            context.SellOrders.RemoveRange(veryOldOrders);
            _logger.LogInformation("Removed {Count} old sell orders (older than 30 days)", veryOldOrders.Count);
        }

        var sellRecords = orders.Select(o => new SellOrderRecord
        {
            ItemId = o.ItemId,
            Price = o.Price,
            Quantity = o.Quantity,
            CollectedAt = o.CollectedAt
        });

        context.SellOrders.AddRange(sellRecords);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved {Count} sell orders to database", orders.Count);
    }

    public async Task<List<Order>> GetLatestOrdersAsync(int itemId, bool isBuyOrder, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (isBuyOrder)
        {
            var records = await context.BuyOrders
                .Where(o => o.ItemId == itemId)
                .OrderByDescending(o => o.CollectedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return records.Select(r => new Order
            {
                ItemId = r.ItemId,
                Price = r.Price,
                Quantity = r.Quantity,
                IsBuyOrder = true,
                CollectedAt = r.CollectedAt
            }).ToList();
        }
        else
        {
            var records = await context.SellOrders
                .Where(o => o.ItemId == itemId)
                .OrderByDescending(o => o.CollectedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return records.Select(r => new Order
            {
                ItemId = r.ItemId,
                Price = r.Price,
                Quantity = r.Quantity,
                IsBuyOrder = false,
                CollectedAt = r.CollectedAt
            }).ToList();
        }
    }

    public async Task<Dictionary<string, object>> GetAnalyticsSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var totalItems = await context.Items.CountAsync(cancellationToken);
        var totalBuyOrders = await context.BuyOrders.CountAsync(cancellationToken);
        var totalSellOrders = await context.SellOrders.CountAsync(cancellationToken);
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
            
            var baseQuery = isBuyOrder
                ? context.BuyOrders
                    .Where(o => o.ItemId == itemId && o.CollectedAt >= cutoffDate)
                    .Select(o => new { o.Price, o.Quantity, o.CollectedAt })
                : context.SellOrders
                    .Where(o => o.ItemId == itemId && o.CollectedAt >= cutoffDate)
                    .Select(o => new { o.Price, o.Quantity, o.CollectedAt });

            var orders = await baseQuery
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
        
        var baseQuery = isBuyOrder
            ? context.BuyOrders
                .Where(o => o.ItemId == itemId && o.CollectedAt >= cutoffDate)
                .Select(o => new { o.Price, o.Quantity, o.CollectedAt })
            : context.SellOrders
                .Where(o => o.ItemId == itemId && o.CollectedAt >= cutoffDate)
                .Select(o => new { o.Price, o.Quantity, o.CollectedAt });

        var orders = await baseQuery
            .OrderBy(o => o.CollectedAt)
            .ToListAsync(cancellationToken);

        return orders.Cast<object>().ToList();
    }
}

