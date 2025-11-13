namespace SteamAnaliticWorker.Models;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public List<BuyOrderRecord> BuyOrders { get; set; } = new();
    public List<SellOrderRecord> SellOrders { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

