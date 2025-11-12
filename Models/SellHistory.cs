namespace SteamAnaliticWorker.Models;

public class SellHistory
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public DateTime SoldAt { get; set; }
    public DateTime CollectedAt { get; set; }
}

