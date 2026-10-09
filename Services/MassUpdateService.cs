using ECommerceAnalytics.Data;
using ECommerceAnalytics.Domain;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Services;

public class MassUpdateService(AppDbContext db)
{
    /// UPDATE واحد على السيرفر (مفيش تحميل للـ Entities).
    public Task<int> AdjustCategoryPricesAsync(int categoryId, decimal percent, CancellationToken ct)
    {
        var factor = 1 + percent / 100m;
        return db.Products.Where(p => p.CategoryId == categoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * factor), ct);
    }

    /// Bulk Update لقائمة جاية من برّه (ملف أسعار من المورد) - بنحدّث عمود Price بس.
    public async Task BulkUpdatePricesAsync(IEnumerable<PriceUpdate> updates, CancellationToken ct)
    {
        var products = updates.Select(u => new Product { Id = u.ProductId, Price = u.Price }).ToList();
        await db.BulkUpdateAsync(products, new BulkConfig
        {
            PropertiesToInclude = new List<string> { nameof(Product.Price) },
            BatchSize = 10_000
        }, cancellationToken: ct);
    }

    /// Batch Operations بدون مكتبة: Keyset batches + ChangeTracker.Clear() لتحرير الذاكرة.
    public async Task<int> ApplyDiscountInBatchesAsync(decimal percent, int batchSize, CancellationToken ct)
    {
        var factor = 1 - percent / 100m;
        var lastId = 0; var total = 0;
        while (true)
        {
            var batch = await db.Products
                .Where(p => p.Id > lastId && p.IsActive)
                .OrderBy(p => p.Id).Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var p in batch) p.Price = Math.Round(p.Price * factor, 2);
            await db.SaveChangesAsync(ct);

            lastId = batch[^1].Id;
            total += batch.Count;
            db.ChangeTracker.Clear();     // بدونها الذاكرة بتكبر مع كل Batch
        }
        return total;
    }

    /// إلغاء الطلبات القديمة المعلّقة على دفعات (UPDATE TOP) لتجنب Lock Escalation وتضخّم الـ Log.
    public async Task<int> CancelStalePendingOrdersAsync(int olderThanDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        int affected, total = 0;
        do
        {
            affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TOP (5000) Orders SET Status = {(byte)OrderStatus.Cancelled} WHERE Status = {(byte)OrderStatus.Pending} AND CreatedAt < {cutoff}", ct);
            total += affected;
        } while (affected > 0);
        return total;
    }
}
