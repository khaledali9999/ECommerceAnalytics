using ECommerceAnalytics.Data;
using ECommerceAnalytics.Domain;
using ECommerceAnalytics.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextPool<AppDbContext>(o =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(30));
    if (builder.Environment.IsDevelopment())
        o.EnableSensitiveDataLogging().LogTo(Console.WriteLine,
            new[] { DbLoggerCategory.Database.Command.Name }, Microsoft.Extensions.Logging.LogLevel.Information);
});
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<QueryService>();
builder.Services.AddScoped<MassUpdateService>();
builder.Services.AddScoped<Seeder>();

var app = builder.Build();

// dotnet run -- seed [orders]
if (args.Length > 0 && args[0] == "seed")
{
    using var scope = app.Services.CreateScope();
    var orders = args.Length > 1 ? int.Parse(args[1]) : 5_000_000;
    await scope.ServiceProvider.GetRequiredService<Seeder>()
        .SeedAsync(orders, customers: Math.Max(1000, orders / 10), products: Math.Max(500, orders / 100), reviews: Math.Max(1000, orders / 5));
    return;
}

app.MapPost("/orders", async (CreateOrderRequest req, OrderService svc, CancellationToken ct) =>
{
    try { return Results.Ok(await svc.CreateAsync(req, ct)); }
    catch (InsufficientStockException e) { return Results.Conflict(e.Message); }
    catch (InvalidOperationException e) { return Results.BadRequest(e.Message); }
});

app.MapGet("/orders", (QueryService q, int page, int pageSize, OrderStatus? status, CancellationToken ct) =>
    q.GetOrdersPageAsync(page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize, status, ct));
app.MapGet("/orders/cursor", (QueryService q, string? cursor, int pageSize, CancellationToken ct) =>
    q.GetOrdersCursorAsync(cursor, pageSize == 0 ? 20 : pageSize, ct));
app.MapGet("/orders/active", (QueryService q, int take, CancellationToken ct) => q.GetActiveOrdersAsync(take == 0 ? 20 : take, ct));
app.MapGet("/customers/{id:int}/orders", (int id, QueryService q, int take, CancellationToken ct) =>
    q.GetCustomerOrdersAsync(id, take == 0 ? 20 : take, ct));
app.MapGet("/products/{id:int}", async (int id, QueryService q) => await q.GetProductAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());
app.MapGet("/products/{id:int}/rating", (int id, QueryService q, CancellationToken ct) => q.GetProductRatingAsync(id, ct));

app.MapGet("/reports/daily-sales", (QueryService q, DateTime from, DateTime to, CancellationToken ct) => q.GetDailySalesAsync(from, to, ct));
app.MapGet("/reports/top-products", (QueryService q, DateTime from, DateTime to, int top, CancellationToken ct) =>
    q.GetTopProductsAsync(from, to, top == 0 ? 20 : top, ct));

app.MapPost("/admin/price-adjust", (MassUpdateService s, int categoryId, decimal percent, CancellationToken ct) => s.AdjustCategoryPricesAsync(categoryId, percent, ct));
app.MapPost("/admin/bulk-update-prices", async (MassUpdateService s, List<PriceUpdate> updates, CancellationToken ct) => { await s.BulkUpdatePricesAsync(updates, ct); return Results.NoContent(); });
app.MapPost("/admin/discount-batches", (MassUpdateService s, decimal percent, CancellationToken ct) => s.ApplyDiscountInBatchesAsync(percent, 1000, ct));
app.MapPost("/admin/cancel-stale-orders", (MassUpdateService s, int days, CancellationToken ct) => s.CancelStalePendingOrdersAsync(days == 0 ? 30 : days, ct));

app.Run();
