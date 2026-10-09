using ECommerceAnalytics.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerceAnalytics.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // منع multiple cascade paths + حذف عرضي لملايين الصفوف
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasIndex(x => x.Email, "UX_Customers_Email").IsUnique();
        });

        b.Entity<Category>().Property(x => x.Name).HasMaxLength(100);

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasIndex(x => new { x.CategoryId, x.IsActive }, "IX_Products_CategoryId_IsActive")
             .IncludeProperties(x => new { x.Price, x.Name });
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.Total).HasPrecision(18, 2);

            // "طلبات العميل الأخيرة": WHERE CustomerId = ? ORDER BY CreatedAt DESC
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt }, "IX_Orders_CustomerId_CreatedAt")
             .IsDescending(false, true)
             .IncludeProperties(x => new { x.Status, x.Total });

            // Pagination (Offset + Cursor) والتقارير حسب التاريخ
            e.HasIndex(x => new { x.CreatedAt, x.Id }, "IX_Orders_CreatedAt_Id")
             .IsDescending(true, true)
             .IncludeProperties(x => new { x.CustomerId, x.Status, x.Total });

            // Filtered Index: الطلبات النشطة (Pending/Processing) ~3% من الجدول
            e.HasIndex(x => new { x.CreatedAt, x.Id }, "IX_Orders_Active_CreatedAt")
             .IsDescending(true, true)
             .IncludeProperties(x => new { x.CustomerId, x.Status, x.Total })
             .HasFilter("[Status] IN (0,1)");
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasIndex(x => x.OrderId, "IX_OrderItems_OrderId")
             .IncludeProperties(x => new { x.ProductId, x.Quantity, x.UnitPrice });
            e.HasIndex(x => x.ProductId, "IX_OrderItems_ProductId")
             .IncludeProperties(x => new { x.OrderId, x.Quantity, x.UnitPrice });
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.OrderId, "IX_Payments_OrderId");
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProductId, x.CreatedAt }, "IX_Reviews_ProductId_CreatedAt")
             .IsDescending(false, true)
             .IncludeProperties(x => x.Rating);
            e.HasIndex(x => x.CustomerId, "IX_Reviews_CustomerId");
        });
    }
}
