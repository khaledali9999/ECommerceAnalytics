using System.Text;
using ECommerceAnalytics.Data;
using ECommerceAnalytics.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Services;

public class QueryService(AppDbContext db)
{
    private const int MaxPageSize = 100;   // يحمي الذاكرة (< 10MB/request)

    // ---------- Offset pagination ----------
    public async Task<PagedResult<OrderListDto>> GetOrdersPageAsync(int page, int pageSize, OrderStatus? status, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var q = db.Orders.AsNoTracking();
        if (status is { } s) q = q.Where(o => o.Status == s);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderListDto(o.Id, o.CreatedAt, o.Status, o.Total, o.Customer.Name))
            .ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    // ---------- Cursor (Keyset) pagination: سرعة ثابتة حتى آخر صفحة ----------
    public async Task<CursorResult<OrderListDto>> GetOrdersCursorAsync(string? cursor, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var q = db.Orders.AsNoTracking();

        if (TryDecode(cursor, out var t, out var id))
            q = q.Where(o => o.CreatedAt < t || (o.CreatedAt == t && o.Id < id));

        var rows = await q
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(pageSize + 1)                       // +1 عشان نعرف في صفحة تالية ولا لأ
            .Select(o => new OrderListDto(o.Id, o.CreatedAt, o.Status, o.Total, o.Customer.Name))
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > pageSize)
        {
            rows.RemoveAt(rows.Count - 1);
            next = Encode(rows[^1].CreatedAt, rows[^1].Id);
        }
        return new(rows, next);
    }

    // ---------- Filtered Index: الطلبات النشطة ----------
    // الـ Status بقيم ثابتة (Constants) مش Parameters، عشان الـ Optimizer يختار الـ Filtered Index
    public Task<List<OrderListDto>> GetActiveOrdersAsync(int take, CancellationToken ct) =>
        db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Processing)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .Select(o => new OrderListDto(o.Id, o.CreatedAt, o.Status, o.Total, o.Customer.Name))
            .ToListAsync(ct);

    // ---------- Compiled ----------
    public Task<ProductDto?> GetProductAsync(int id) => CompiledQueries.ProductById(db, id);

    public async Task<List<OrderListDto>> GetCustomerOrdersAsync(int customerId, int take, CancellationToken ct)
    {
        var list = new List<OrderListDto>();
        await foreach (var o in CompiledQueries.CustomerOrders(db, customerId, Math.Clamp(take, 1, MaxPageSize)).WithCancellation(ct))
            list.Add(o);
        return list;
    }

    public Task<ProductRatingDto?> GetProductRatingAsync(int productId, CancellationToken ct) =>
        db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId)
            .GroupBy(r => r.ProductId)
            .Select(g => new ProductRatingDto(g.Key, g.Average(r => (double)r.Rating), g.Count()))
            .FirstOrDefaultAsync(ct);

    // ---------- تقارير الأدمن (Query واحدة لكل تقرير) ----------
    public Task<List<DailySalesDto>> GetDailySalesAsync(DateTime from, DateTime to, CancellationToken ct) =>
        db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt < to && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new DailySalesDto(g.Key, g.Count(), g.Sum(o => o.Total)))
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

    public Task<List<TopProductDto>> GetTopProductsAsync(DateTime from, DateTime to, int top, CancellationToken ct) =>
        (from i in db.OrderItems.AsNoTracking()
         join o in db.Orders on i.OrderId equals o.Id
         where o.CreatedAt >= from && o.CreatedAt < to && o.Status != OrderStatus.Cancelled
         group i by new { i.ProductId, i.Product.Name } into g
         orderby g.Sum(x => x.Quantity * x.UnitPrice) descending
         select new TopProductDto(g.Key.ProductId, g.Key.Name, g.Sum(x => x.Quantity), g.Sum(x => x.Quantity * x.UnitPrice)))
        .Take(Math.Clamp(top, 1, 100))
        .ToListAsync(ct);

    // ---------- Cursor helpers ----------
    private static string Encode(DateTime t, long id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{t.Ticks}|{id}"));

    private static bool TryDecode(string? s, out DateTime t, out long id)
    {
        t = default; id = 0;
        if (string.IsNullOrEmpty(s)) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split('|');
            t = new DateTime(long.Parse(parts[0]), DateTimeKind.Unspecified);
            id = long.Parse(parts[1]);
            return true;
        }
        catch { return false; }
    }
}
