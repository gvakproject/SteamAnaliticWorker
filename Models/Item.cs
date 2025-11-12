namespace SteamAnaliticWorker.Models;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public List<SellHistory> SellHistory { get; set; } = new();
    public List<Order> SellOrders { get; set; } = new();
    public List<Order> BuyOrders { get; set; } = new();
    public decimal PurchasePrice { get; set; }
    public decimal ExpectedSellPrice { get; set; }
    public bool ShouldBuy { get; set; }
    public DateTime LastUpdated { get; set; }
}

