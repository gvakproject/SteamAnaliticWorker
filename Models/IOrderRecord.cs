namespace SteamAnaliticWorker.Models; 

public interface IOrderRecord
{
    int ItemId { get; set; }
    decimal Price { get; set; }
    int Quantity { get; set; }
    DateTime CollectedAt { get; set; }
}
