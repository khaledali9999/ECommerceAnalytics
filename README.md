# E-Commerce Analytics Database — Database & EF Performance

نظام متجر إلكتروني (ASP.NET Core 8 + EF Core 8 + SQL Server) مصمم يتحمل:
**5M Orders · 10M OrderItems · 500K Customers · 50K Products · ~10K طلب/يوم · تقارير على سنتين.**

## التشغيل

```bash
# 1) عدّل ConnectionStrings:Default في appsettings.json
# 2) Seeding (5M orders - بياخد دقايق قليلة بـ SqlBulkCopy). للتجربة السريعة: seed 200000
dotnet run -c Release -- seed 5000000
# 3) (اختياري) RCSI + Query Store
#    شغّل Sql/03_database_settings.sql
# 4) شغّل الـ API (SQL Logging شغال في Development)
dotnet run
```

خطوات الـ Seeding: إنشاء الـ Schema ← **حذف الـ Indexes** ← Bulk Insert بـ chunks (100K) ← **بناء الـ Indexes** ← `sp_updatestats`.
(بناء الـ Index مرة واحدة بعد الـ Insert أسرع بكتير من صيانته مع كل صف.)

## هيكل المشروع

| المسار | الغرض |
|---|---|
| `Domain/Entities.cs` | Customer, Category, Product, Order, OrderItem, Payment, Review |
| `Data/AppDbContext.cs` | الـ Model + كل الـ Indexes |
| `Data/CompiledQueries.cs` | Compiled Queries (ProductById, CustomerOrders) |
| `Data/Seeder.cs` | Bulk seeding بـ EFCore.BulkExtensions |
| `Services/OrderService.cs` | Transaction + Retry للـ Deadlocks |
| `Services/QueryService.cs` | Pagination (Offset/Cursor) + التقارير |
| `Services/MassUpdateService.cs` | ExecuteUpdate / BulkUpdate / Batches + `ChangeTracker.Clear()` |
| `Sql/` | Indexes، Before/After، إعدادات DB، Bonus |

## الـ Indexes (ولماذا)

| Index | الأعمدة | بيخدم |
|---|---|---|
| `IX_Orders_CustomerId_CreatedAt` | (CustomerId, CreatedAt DESC) INCLUDE (Status, Total) | طلبات عميل، الأحدث أولاً. Composite: العمود الـ Equality الأول ثم الـ Sort |
| `IX_Orders_CreatedAt_Id` | (CreatedAt DESC, Id DESC) INCLUDE (CustomerId, Status, Total) | Pagination + Cursor + تقارير بالتاريخ (Covering) |
| `IX_Orders_Active_CreatedAt` | **Filtered**: `WHERE Status IN (0,1)` | الطلبات النشطة (~3%) — Index صغير جدًا |
| `IX_OrderItems_OrderId` | (OrderId) INCLUDE (ProductId, Quantity, UnitPrice) | عناصر الطلب بدون Key Lookup |
| `IX_OrderItems_ProductId` | (ProductId) INCLUDE (OrderId, Quantity, UnitPrice) | تقارير المنتجات |
| `IX_Payments_OrderId` | (OrderId) | Join/FK |
| `IX_Reviews_ProductId_CreatedAt` | (ProductId, CreatedAt DESC) INCLUDE (Rating) | تقييم المنتج (Covering) |
| `IX_Products_CategoryId_IsActive` | (CategoryId, IsActive) INCLUDE (Price, Name) | تصفح الأقسام |
| `UX_Customers_Email` | Unique | Login/Unique |

**Leftmost Prefix:** `IX_Orders_CustomerId_CreatedAt` بيخدم `WHERE CustomerId=?` أو `CustomerId=? AND CreatedAt..` لكن **مش** `WHERE CreatedAt..` لوحدها (لذلك فيه `IX_Orders_CreatedAt_Id`).

**Filtered Index:** الـ Query لازم تستخدم `Status` كـ Constant (مش Parameter) عشان الـ Optimizer يختاره — `GetActiveOrdersAsync` معمول كده.

## Transactions & Isolation

| العملية | Isolation | السبب |
|---|---|---|
| إنشاء Order (Order+Items+Payment) | `ReadCommitted` | المخزون بيتخصم بـ `UPDATE ... WHERE Stock >= qty` (atomic)، فمفيش داعي لـ Serializable |
| القراءات والتقارير | `READ_COMMITTED_SNAPSHOT` (RCSI) | القراءة ما بتتعطلش بالكتابة |
| إلغاء الطلبات القديمة | `ReadCommitted` + دفعات 5000 | تجنب Lock Escalation |

**منع الـ Deadlocks:** ترتيب الوصول ثابت دايمًا: `Products (مرتبة بـ Id) → Orders → OrderItems → Payments`، والـ Transaction قصيرة، و**Retry** (حتى 4 محاولات، Exponential backoff + jitter) على SQL Error 1205.

## EF Performance Checklist

- [x] `AsNoTracking()` في كل القراءات
- [x] Projection لـ DTOs في كل الاستعلامات (مفيش `Select *`)
- [x] لا N+1: الـ `Customer.Name` بيتجاب بـ JOIN داخل نفس الـ Query
- [x] SQL Logging في Development (`appsettings.Development.json` + `LogTo`)
- [x] Compiled Queries: `ProductById`, `CustomerOrders`
- [x] Server-side pagination، `pageSize` أقصاه 100 (حماية الذاكرة)
- [x] Cursor pagination: `GET /orders/cursor?cursor=...`
- [x] Bulk Insert (Seeding)، Bulk Update (`/admin/bulk-update-prices`)، `ExecuteUpdate`، Batches + `ChangeTracker.Clear()`
- [x] كل عمليات الـ DB `async/await` مع `CancellationToken`

## قياس قبل/بعد

شغّل `Sql/02_before_after_demo.sql` في SSMS (Actual Execution Plan + `STATISTICS IO`). املأ الجدول بأرقامك:

| Query | Reads قبل | Reads بعد | الوقت قبل | الوقت بعد | Plan قبل → بعد |
|---|---|---|---|---|---|
| طلبات عميل (Top 20) | | | | | Clustered Scan → Index Seek |
| أحدث 20 طلب | | | | | Scan + Sort → Seek |
| الطلبات النشطة | | | | | Scan → Seek (Filtered) |
| عناصر طلب | | | | | Scan → Seek |
| Cursor page | | | | | — → Seek |

## ملاحظات أداء صريحة

- هدف **< 100 Logical Reads** منطقي للـ OLTP queries (Seek + Top 20). مع JOIN للـ Customer، كل صف بيزود ~3 reads، فاستخدم `pageSize` ≤ 20 للالتزام بالحد.
- `COUNT(*)` في Offset pagination على 5M صف مكلف؛ لو فوق الحد، اعتمد على Cursor pagination أو count تقريبي.
- تقارير سنتين كاملة مش هتنزل تحت 100 reads (بتجمّع ملايين الصفوف). الحل: تقارير بمدى زمني، أو جدول تجميعي/Indexed View (`DailySales`) يتحدث بالليل.

## Bonus

`Sql/04_bonus.sql`: Temporal Table على Orders، Partitioning شهري (خطوات)، Query Store وDMV لاقتراح Indexes.

## Endpoints

`POST /orders` · `GET /orders` · `GET /orders/cursor` · `GET /orders/active` · `GET /customers/{id}/orders` · `GET /products/{id}` · `GET /products/{id}/rating` · `GET /reports/daily-sales` · `GET /reports/top-products` · `POST /admin/price-adjust` · `POST /admin/bulk-update-prices` · `POST /admin/discount-batches` · `POST /admin/cancel-stale-orders`
