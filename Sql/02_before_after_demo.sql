-- شغّله في SSMS مع "Include Actual Execution Plan" (Ctrl+M).
-- الفكرة: نقيس كل Query بدون Index (Logical Reads) وبعدين بعد الـ Index.
-- ملاحظة: غيّر @cust وتواريخ الـ Reports لقيم موجودة عندك.

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
DECLARE @cust INT = 12345;

PRINT '################ BEFORE (no indexes) ################';
-- شيل الـ Indexes:
-- (شغّل Sql/00_drop_indexes.sql أو الأسطر اللي تحت)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Orders_CustomerId_CreatedAt') DROP INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Orders_CreatedAt_Id')         DROP INDEX IX_Orders_CreatedAt_Id ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Orders_Active_CreatedAt')     DROP INDEX IX_Orders_Active_CreatedAt ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_OrderItems_OrderId')          DROP INDEX IX_OrderItems_OrderId ON dbo.OrderItems;
GO
DECLARE @cust INT = 12345;

-- Q1: طلبات عميل
SELECT TOP (20) o.Id, o.CreatedAt, o.Status, o.Total FROM dbo.Orders o WHERE o.CustomerId = @cust ORDER BY o.CreatedAt DESC;
-- Q2: أحدث 20 طلب (Pagination)
SELECT TOP (20) o.Id, o.CreatedAt, o.Status, o.Total FROM dbo.Orders o ORDER BY o.CreatedAt DESC, o.Id DESC;
-- Q3: الطلبات النشطة
SELECT TOP (20) o.Id, o.CreatedAt, o.Total FROM dbo.Orders o WHERE o.Status IN (0,1) ORDER BY o.CreatedAt DESC, o.Id DESC;
-- Q4: عناصر طلب
SELECT i.ProductId, i.Quantity, i.UnitPrice FROM dbo.OrderItems i WHERE i.OrderId = 4000000;
GO

PRINT '################ AFTER (indexes) ################';
CREATE INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders (CustomerId ASC, CreatedAt DESC) INCLUDE (Status, Total);
CREATE INDEX IX_Orders_CreatedAt_Id ON dbo.Orders (CreatedAt DESC, Id DESC) INCLUDE (CustomerId, Status, Total);
CREATE INDEX IX_Orders_Active_CreatedAt ON dbo.Orders (CreatedAt DESC, Id DESC) INCLUDE (CustomerId, Status, Total) WHERE Status IN (0,1);
CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems (OrderId) INCLUDE (ProductId, Quantity, UnitPrice);
GO
DECLARE @cust INT = 12345;

SELECT TOP (20) o.Id, o.CreatedAt, o.Status, o.Total FROM dbo.Orders o WHERE o.CustomerId = @cust ORDER BY o.CreatedAt DESC;
SELECT TOP (20) o.Id, o.CreatedAt, o.Status, o.Total FROM dbo.Orders o ORDER BY o.CreatedAt DESC, o.Id DESC;
SELECT TOP (20) o.Id, o.CreatedAt, o.Total FROM dbo.Orders o WHERE o.Status IN (0,1) ORDER BY o.CreatedAt DESC, o.Id DESC;
SELECT i.ProductId, i.Quantity, i.UnitPrice FROM dbo.OrderItems i WHERE i.OrderId = 4000000;
GO

-- Composite order demo (Leftmost Prefix): ده Seek ✅
SELECT COUNT(*) FROM dbo.Orders WHERE CustomerId = 12345 AND CreatedAt >= DATEADD(MONTH,-6,SYSUTCDATETIME());
-- ده مش هيعمل Seek على IX_Orders_CustomerId_CreatedAt ❌ (متخطي أول عمود)
SELECT COUNT(*) FROM dbo.Orders WHERE CreatedAt >= DATEADD(MONTH,-6,SYSUTCDATETIME()) AND CustomerId IS NOT NULL;
