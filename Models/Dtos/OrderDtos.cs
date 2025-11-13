namespace SteamAnaliticWorker.Models.Dtos;

public record OrderSnapshot(decimal Price, int Quantity, DateTime CollectedAt);

public record GroupedOrders(DateTime Time, decimal AvgPrice, decimal MinPrice, decimal MaxPrice, int TotalQuantity, int OrderCount);

public record OrderSeries(string Type, IReadOnlyList<GroupedOrders> Data);
