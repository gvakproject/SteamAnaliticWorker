using System.Net.Http;
using System.Text.Json;
using SteamAnaliticWorker.Models;

namespace SteamAnaliticWorker.Services;

public class SteamAnalyticsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamAnalyticsService> _logger;

    public SteamAnalyticsService(HttpClient httpClient, ILogger<SteamAnalyticsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Настройка HttpClient для работы с Steam API
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    public async Task<List<Order>> GetItemOrdersAsync(Item item, bool isBuyOrder, CancellationToken cancellationToken = default)
    {
        var orders = new List<Order>();
        
        try
        {
            var url = $"https://steamcommunity.com/market/itemordershistogram?" +
                $"country=KZ" +
                $"&language=russian" +
                $"&currency=37" +
                $"&item_nameid={item.ItemId}" +
                $"&norender=1";

            var response = await GetResponseAsync(url, cancellationToken: cancellationToken);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

            var jsonProperty = isBuyOrder ? "buy_order_graph" : "sell_order_graph";
            orders = await GetOrdersAsync(json, jsonProperty, item.Id, isBuyOrder);
            
            _logger.LogInformation(
                "Collected {Count} {Type} orders for {ItemName} (ItemId: {ItemId})",
                orders.Count,
                isBuyOrder ? "buy" : "sell",
                item.Name,
                item.ItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders for item {ItemName} (ItemId: {ItemId})", item.Name, item.ItemId);
        }

        return orders;
    }

    private Task<List<Order>> GetOrdersAsync(JsonDocument json, string jsonProperty, int itemId, bool isBuyOrder)
    {
        var orders = new List<Order>();
        var now = DateTime.UtcNow;

        if (json.RootElement.TryGetProperty(jsonProperty, out var orderResult))
        {
            int prevQuantity = 0;
            foreach (var order in orderResult.EnumerateArray())
            {
                if (order.GetArrayLength() >= 2)
                {
                    int cumulative = order[1].GetInt32();
                    int realQuantity = cumulative - prevQuantity;
                    prevQuantity = cumulative;

                    if (realQuantity > 0) // Сохраняем только заказы с количеством > 0
                    {
                        orders.Add(new Order
                        {
                            ItemId = itemId,
                            Price = order[0].GetDecimal(),
                            Quantity = realQuantity,
                            IsBuyOrder = isBuyOrder,
                            CollectedAt = now
                        });
                    }
                }
            }
        }

        return Task.FromResult(orders);
    }

    private async Task<HttpResponseMessage> GetResponseAsync(
        string url,
        int maxRetries = 3,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.ConnectionClose = true;
                
                var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                
                return response;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[Attempt {Attempt}] Request to {Url} timed out.", attempt, url);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("[Attempt {Attempt}] HTTP error for {Url}: {Message}", attempt, url, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Attempt {Attempt}] Unexpected error for {Url}: {Message}", attempt, url, ex.Message);
            }
            
            if (attempt < maxRetries)
                await Task.Delay(1000 * attempt, cancellationToken);
        }
        
        throw new HttpRequestException($"Failed to get a successful response from {url} after {maxRetries} attempts.");
    }
}

