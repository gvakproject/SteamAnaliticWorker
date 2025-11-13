namespace SteamAnaliticWorker.Models;

public class SellOrderRecord : IOrderRecord
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public DateTime CollectedAt { get; set; }

    public Item? Item { get; set; }
}

