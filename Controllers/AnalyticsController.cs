using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamAnaliticWorker.Data;
using SteamAnaliticWorker.Models;
using SteamAnaliticWorker.Services;

namespace SteamAnaliticWorker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly DataStorageService _storageService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        DataStorageService storageService,
        ILogger<AnalyticsController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _storageService = storageService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await _storageService.GetAnalyticsSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems(CancellationToken cancellationToken)
    {
        var items = await _storageService.GetAllItemsAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("items/{itemId}/orders")]
    public async Task<IActionResult> GetItemOrders(
        int itemId,
        [FromQuery] bool? buyOrders = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var query = context.Orders.Where(o => o.ItemId == itemId);
        
        if (buyOrders.HasValue)
        {
            query = query.Where(o => o.IsBuyOrder == buyOrders.Value);
        }
        
        var orders = await query
            .OrderByDescending(o => o.CollectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        
        return Ok(orders);
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] ItemDto itemDto, CancellationToken cancellationToken)
    {
        var item = new Item
        {
            Name = itemDto.Name,
            ItemId = itemDto.ItemId
        };
        
        var savedItem = await _storageService.AddOrUpdateItemAsync(item, cancellationToken);
        return Ok(savedItem);
    }

    [HttpGet("items/{itemId}/analytics")]
    public async Task<IActionResult> GetItemAnalytics(int itemId, CancellationToken cancellationToken)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var item = await context.Items.FindAsync(new object[] { itemId }, cancellationToken);
        if (item == null)
        {
            return NotFound();
        }

        var buyOrders = await context.Orders
            .Where(o => o.ItemId == itemId && o.IsBuyOrder)
            .OrderBy(o => o.Price)
            .ToListAsync(cancellationToken);

        var sellOrders = await context.Orders
            .Where(o => o.ItemId == itemId && !o.IsBuyOrder)
            .OrderByDescending(o => o.Price)
            .ToListAsync(cancellationToken);

        var analytics = new
        {
            item = new { item.Id, item.Name, item.ItemId, item.LastUpdated },
            buyOrders = new
            {
                count = buyOrders.Count,
                minPrice = buyOrders.Any() ? buyOrders.Min(o => o.Price) : (decimal?)null,
                maxPrice = buyOrders.Any() ? buyOrders.Max(o => o.Price) : (decimal?)null,
                totalQuantity = buyOrders.Sum(o => o.Quantity),
                orders = buyOrders.Take(20).Select(o => new { o.Price, o.Quantity, o.CollectedAt })
            },
            sellOrders = new
            {
                count = sellOrders.Count,
                minPrice = sellOrders.Any() ? sellOrders.Min(o => o.Price) : (decimal?)null,
                maxPrice = sellOrders.Any() ? sellOrders.Max(o => o.Price) : (decimal?)null,
                totalQuantity = sellOrders.Sum(o => o.Quantity),
                orders = sellOrders.Take(20).Select(o => new { o.Price, o.Quantity, o.CollectedAt })
            }
        };

        return Ok(analytics);
    }

    [HttpGet("items/{itemId}/time-series")]
    public async Task<IActionResult> GetItemTimeSeries(
        int itemId,
        [FromQuery] bool? buyOrders = null,
        [FromQuery] string grouping = "hour",
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var result = new List<object>();

        if (!buyOrders.HasValue || buyOrders.Value)
        {
            var buyData = await _storageService.GetOrdersByTimeGroupingAsync(
                itemId, true, grouping, days, cancellationToken);
            result.Add(new { type = "buy", data = buyData });
        }

        if (!buyOrders.HasValue || !buyOrders.Value)
        {
            var sellData = await _storageService.GetOrdersByTimeGroupingAsync(
                itemId, false, grouping, days, cancellationToken);
            result.Add(new { type = "sell", data = sellData });
        }

        return Ok(result);
    }

    [HttpGet("items/{itemId}/price-history")]
    public async Task<IActionResult> GetItemPriceHistory(
        int itemId,
        [FromQuery] bool? buyOrders = null,
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>();

        if (!buyOrders.HasValue || buyOrders.Value)
        {
            var buyHistory = await _storageService.GetPriceHistoryAsync(
                itemId, true, days, cancellationToken);
            result["buy"] = buyHistory;
        }

        if (!buyOrders.HasValue || !buyOrders.Value)
        {
            var sellHistory = await _storageService.GetPriceHistoryAsync(
                itemId, false, days, cancellationToken);
            result["sell"] = sellHistory;
        }

        return Ok(result);
    }

    [HttpPost("collect")]
    public Task<IActionResult> TriggerCollection(CancellationToken cancellationToken)
    {
        try
        {
            // Запускаем сбор данных в фоне
            _ = Task.Run(async () =>
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var steamService = scope.ServiceProvider.GetRequiredService<SteamAnalyticsService>();
                var storageService = scope.ServiceProvider.GetRequiredService<DataStorageService>();

                var items = await storageService.GetAllItemsAsync(cancellationToken);

                if (items.Count == 0)
                {
                    _logger.LogWarning("No items configured for manual collection");
                    return;
                }

                foreach (var item in items)
                {
                    try
                    {
                        var buyOrders = await steamService.GetItemOrdersAsync(item, isBuyOrder: true, cancellationToken);
                        if (buyOrders.Count > 0)
                        {
                            await storageService.SaveOrdersAsync(buyOrders, cancellationToken);
                        }

                        var sellOrders = await steamService.GetItemOrdersAsync(item, isBuyOrder: false, cancellationToken);
                        if (sellOrders.Count > 0)
                        {
                            await storageService.SaveOrdersAsync(sellOrders, cancellationToken);
                        }

                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing item {ItemName} in manual collection", item.Name);
                    }
                }

                _logger.LogInformation("Manual collection completed");
            }, cancellationToken);

            return Task.FromResult<IActionResult>(Ok(new { message = "Collection started", timestamp = DateTime.UtcNow }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting manual collection");
            return Task.FromResult<IActionResult>(StatusCode(500, new { error = "Failed to start collection", message = ex.Message }));
        }
    }
}

public class ItemDto
{
    public string Name { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
}

