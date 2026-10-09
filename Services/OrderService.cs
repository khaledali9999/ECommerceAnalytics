using System.Data;
using ECommerceAnalytics.Data;
using ECommerceAnalytics.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Services;

public class OrderService(AppDbContext db, ILogger<OrderService> log)
{
    private const int MaxAttempts = 4;
    private const int SqlDeadlock = 1205;

    /// Retry Logic للـ Deadlocks (Exponential backoff + jitter).
    public async Task<CreatedOrderDto> CreateAsync(CreateOrderRequest req, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await CreateCoreAsync(req, ct); }
            catch (Exception ex) when (IsDeadlock(ex) && attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
                var delay = 50 * Math.Pow(2, attempt) + Random.Shared.Next(0, 50);
                log.LogWarning("Deadlock (attempt {Attempt}), retrying in {Delay}ms", attempt, delay);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
            }
        }
    }

    private async Task<CreatedOrderDto> CreateCoreAsync(CreateOrderRequest req, CancellationToken ct)
    {
        // دمج الأصناف المكررة + ترتيب ثابت بـ ProductId => نفس ترتيب الـ Locks في كل Transaction
        var lines = req.Items.GroupBy(i => i.ProductId)
            .Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(l => l.ProductId).ToList();
        var ids = lines.Select(l => l.ProductId).ToList();

        // ReadCommitted كفاية: التحقق من المخزون بيتم atomically جوه UPDATE نفسه
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // الأسعار (Projection + AsNoTracking) - Query واحدة
        var prices = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .Select(p => new { p.Id, p.Price })
            .ToDictionaryAsync(p => p.Id, p => p.Price, ct);
        if (prices.Count != ids.Count) throw new InvalidOperationException("Unknown or inactive product");

        if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId && c.IsActive, ct))
            throw new InvalidOperationException("Unknown customer");

        // ترتيب الوصول للجداول دايمًا: Products -> Orders -> OrderItems -> Payments
        foreach (var l in lines)
        {
            var qty = l.Quantity;
            var rows = await db.Products
                .Where(p => p.Id == l.ProductId && p.Stock >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - qty), ct);
            if (rows == 0) throw new InsufficientStockException(l.ProductId);
        }

        var now = DateTime.UtcNow;
        var order = new Order
        {
            CustomerId = req.CustomerId,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            Items = lines.Select(l => new OrderItem
            {
                ProductId = l.ProductId, Quantity = l.Quantity, UnitPrice = prices[l.ProductId]
            }).ToList()
        };
        order.Total = order.Items.Sum(i => i.Quantity * i.UnitPrice);
        order.Payment = new Payment { Amount = order.Total, Method = req.PaymentMethod, PaidAt = now };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);   // Batched INSERTs
        await tx.CommitAsync(ct);
        return new CreatedOrderDto(order.Id, order.Total);
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is SqlException { Number: SqlDeadlock }) return true;
        return false;
    }
}
