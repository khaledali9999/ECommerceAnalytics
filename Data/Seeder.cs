using System.Diagnostics;
using ECommerceAnalytics.Domain;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Data;

public class Seeder(AppDbContext db)
{
    private const int Chunk = 100_000;
    private static readonly BulkConfig Cfg = new() { KeepIdentity = true, BatchSize = 50_000, BulkCopyTimeout = 0, PreserveInsertOrder = false };

    public async Task SeedAsync(int orders = 5_000_000, int customers = 500_000, int products = 50_000, int reviews = 1_000_000)
    {
        var sw = Stopwatch.StartNew();
        db.Database.SetCommandTimeout(TimeSpan.FromHours(2));
        await db.Database.EnsureCreatedAsync();
        if (await db.Orders.AnyAsync()) { Console.WriteLine("Already seeded."); return; }

        // نشيل الـ Non-clustered indexes قبل الـ Insert الضخم (أسرع بكتير) ونبنيهم بعده
        await RunScriptAsync("00_drop_indexes.sql");

        var rnd = new Random(42);
        var now = DateTime.UtcNow;

        await db.BulkInsertAsync(Enumerable.Range(1, 50).Select(i => new Category { Id = i, Name = $"Category {i}" }).ToList(), Cfg);

        for (var start = 1; start <= customers; start += Chunk)
        {
            var list = Enumerable.Range(start, Math.Min(Chunk, customers - start + 1)).Select(i => new Customer
            {
                Id = i, Name = $"Customer {i}", Email = $"user{i}@example.com",
                IsActive = rnd.Next(100) < 95, CreatedAt = now.AddDays(-rnd.Next(1, 1500))
            }).ToList();
            await db.BulkInsertAsync(list, Cfg);
        }
        Log("customers", sw);

        for (var start = 1; start <= products; start += Chunk)
        {
            var list = Enumerable.Range(start, Math.Min(Chunk, products - start + 1)).Select(i => new Product
            {
                Id = i, Name = $"Product {i}", CategoryId = rnd.Next(1, 51),
                Price = Math.Round((decimal)(rnd.NextDouble() * 990 + 10), 2),
                Stock = rnd.Next(100, 100_000), IsActive = rnd.Next(100) < 97
            }).ToList();
            await db.BulkInsertAsync(list, Cfg);
        }
        Log("products", sw);

        var priceOf = await db.Products.AsNoTracking().Select(p => new { p.Id, p.Price }).ToDictionaryAsync(p => p.Id, p => p.Price);

        long itemId = 1, payId = 1;
        for (long start = 1; start <= orders; start += Chunk)
        {
            var n = (int)Math.Min(Chunk, orders - start + 1);
            var os = new List<Order>(n); var items = new List<OrderItem>(n * 2); var pays = new List<Payment>(n);
            for (var k = 0; k < n; k++)
            {
                var id = start + k;
                var created = now.AddSeconds(-rnd.NextInt64(0, 730L * 24 * 3600));   // سنتين
                var r = rnd.Next(100);
                var status = r < 2 ? OrderStatus.Pending : r < 3 ? OrderStatus.Processing :
                             r < 8 ? OrderStatus.Cancelled : r < 18 ? OrderStatus.Shipped : OrderStatus.Delivered;
                decimal total = 0;
                for (var j = 0; j < 2; j++)       // 2 items/order => 10M items
                {
                    var pid = rnd.Next(1, products + 1); var qty = rnd.Next(1, 5);
                    total += qty * priceOf[pid];
                    items.Add(new OrderItem { Id = itemId++, OrderId = id, ProductId = pid, Quantity = qty, UnitPrice = priceOf[pid] });
                }
                os.Add(new Order { Id = id, CustomerId = rnd.Next(1, customers + 1), Status = status, Total = total, CreatedAt = created });
                pays.Add(new Payment { Id = payId++, OrderId = id, Amount = total, Method = (PaymentMethod)rnd.Next(3), PaidAt = created });
            }
            await db.BulkInsertAsync(os, Cfg);
            await db.BulkInsertAsync(items, Cfg);
            await db.BulkInsertAsync(pays, Cfg);
            if (start % (Chunk * 10) == 1) Log($"orders up to {start + n - 1:N0}", sw);
        }

        for (long start = 1; start <= reviews; start += Chunk)
        {
            var n = (int)Math.Min(Chunk, reviews - start + 1);
            var list = Enumerable.Range(0, n).Select(k => new Review
            {
                Id = start + k, ProductId = rnd.Next(1, products + 1), CustomerId = rnd.Next(1, customers + 1),
                Rating = (byte)rnd.Next(1, 6), Comment = "ok", CreatedAt = now.AddDays(-rnd.Next(1, 730))
            }).ToList();
            await db.BulkInsertAsync(list, Cfg);
        }
        Log("reviews", sw);

        await RunScriptAsync("01_create_indexes.sql");
        await db.Database.ExecuteSqlRawAsync("EXEC sp_updatestats");
        Log("indexes + stats", sw);
    }

    private Task RunScriptAsync(string file) =>
        db.Database.ExecuteSqlRawAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", file)));

    private static void Log(string what, Stopwatch sw) => Console.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss}] {what} done");
}
