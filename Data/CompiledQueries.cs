using ECommerceAnalytics.Services;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Data;

/// Queries بتتكرر آلاف المرات في الثانية -> نعملها Compile مرة واحدة.
public static class CompiledQueries
{
    public static readonly Func<AppDbContext, int, Task<ProductDto?>> ProductById =
        EF.CompileAsyncQuery((AppDbContext c, int id) =>
            c.Products.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new ProductDto(p.Id, p.Name, p.Category.Name, p.Price, p.Stock))
                .FirstOrDefault());

    public static readonly Func<AppDbContext, int, int, IAsyncEnumerable<OrderListDto>> CustomerOrders =
        EF.CompileAsyncQuery((AppDbContext c, int customerId, int take) =>
            c.Orders.AsNoTracking()
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(take)
                .Select(o => new OrderListDto(o.Id, o.CreatedAt, o.Status, o.Total, o.Customer.Name)));
}
