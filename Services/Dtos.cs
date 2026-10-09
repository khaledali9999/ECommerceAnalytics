using ECommerceAnalytics.Domain;

namespace ECommerceAnalytics.Services;

public record OrderListDto(long Id, DateTime CreatedAt, OrderStatus Status, decimal Total, string CustomerName);
public record ProductDto(int Id, string Name, string Category, decimal Price, int Stock);
public record ProductRatingDto(int ProductId, double AverageRating, int ReviewCount);
public record DailySalesDto(DateTime Day, int Orders, decimal Revenue);
public record TopProductDto(int ProductId, string Name, int Units, decimal Revenue);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public record CursorResult<T>(IReadOnlyList<T> Items, string? NextCursor);

public record OrderLine(int ProductId, int Quantity);
public record CreateOrderRequest(int CustomerId, PaymentMethod PaymentMethod, List<OrderLine> Items);
public record CreatedOrderDto(long OrderId, decimal Total);
public record PriceUpdate(int ProductId, decimal Price);

public class InsufficientStockException(int productId) : Exception($"Insufficient stock for product {productId}");
