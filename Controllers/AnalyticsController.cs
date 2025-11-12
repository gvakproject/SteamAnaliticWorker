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
}

public class ItemDto
{
    public string Name { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
}

