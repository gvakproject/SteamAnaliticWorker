using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SteamAnaliticWorker.Data;
using SteamAnaliticWorker.Models;
using SteamAnaliticWorker.Models.Dtos;
using SteamAnaliticWorker.Services;

namespace SteamAnaliticWorker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly DataStorageService _storageService;
    private readonly ILogger<AnalyticsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AnalyticsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        DataStorageService storageService,
        ILogger<AnalyticsController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _dbContextFactory = dbContextFactory;
        _storageService = storageService;
        _logger = logger;
        _scopeFactory = scopeFactory;
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
        var takeLimit = Math.Max(1, limit);

        if (buyOrders.HasValue)
        {
            var filtered = await _storageService.GetLatestOrdersAsync(itemId, buyOrders.Value, takeLimit, cancellationToken);
            return Ok(filtered);
        }
        else
        {
            var buy = await _storageService.GetLatestOrdersAsync(itemId, true, takeLimit, cancellationToken);
            var sell = await _storageService.GetLatestOrdersAsync(itemId, false, takeLimit, cancellationToken);

            var combined = buy.Concat(sell)
                .OrderByDescending(o => o.CollectedAt)
                .Take(takeLimit)
                .ToList();

            return Ok(combined);
        }
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
        try
        {
            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            var item = await context.Items.FindAsync(new object[] { itemId }, cancellationToken);
            if (item == null)
            {
                return NotFound(new { error = "Item not found" });
            }

            // SQLite не поддерживает сортировку по decimal в ORDER BY, поэтому загружаем данные и сортируем в памяти
            var buyOrdersRaw = await context.BuyOrders
                .Where(o => o.ItemId == itemId)
                .ToListAsync(cancellationToken);
            var buyOrders = buyOrdersRaw.OrderBy(o => o.Price).ToList();

            var sellOrdersRaw = await context.SellOrders
                .Where(o => o.ItemId == itemId)
                .ToListAsync(cancellationToken);
            var sellOrders = sellOrdersRaw.OrderByDescending(o => o.Price).ToList();

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analytics for item {ItemId}", itemId);
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpGet("items/{itemId}/time-series")]
    public async Task<IActionResult> GetItemTimeSeries(
        int itemId,
        [FromQuery] bool? buyOrders = null,
        [FromQuery] string grouping = "hour",
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Проверяем существование предмета
            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var item = await context.Items.FindAsync(new object[] { itemId }, cancellationToken);
            if (item == null)
            {
                return NotFound(new { error = "Item not found" });
            }

            var result = new List<OrderSeries>();

            if (!buyOrders.HasValue || buyOrders.Value)
            {
                var buyData = await _storageService.GetOrdersByTimeGroupingAsync(
                    itemId, true, grouping, days, cancellationToken);
                result.Add(new OrderSeries("buy", buyData));
            }

            if (!buyOrders.HasValue || !buyOrders.Value)
            {
                var sellData = await _storageService.GetOrdersByTimeGroupingAsync(
                    itemId, false, grouping, days, cancellationToken);
                result.Add(new OrderSeries("sell", sellData));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting time-series for item {ItemId}", itemId);
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpGet("items/{itemId}/price-history")]
    public async Task<IActionResult> GetItemPriceHistory(
        int itemId,
        [FromQuery] bool? buyOrders = null,
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<OrderSnapshot>>();

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
            var scopeFactory = _scopeFactory;

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ICollectionOrchestrator>();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                try
                {
                    await orchestrator.CollectAsync(cts.Token);
                    _logger.LogInformation("Manual collection completed");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Manual collection cancelled or timed out");
                }
            }, CancellationToken.None);

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

