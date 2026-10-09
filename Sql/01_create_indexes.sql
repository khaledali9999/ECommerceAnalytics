-- Idempotent: ينفع تتشغل أكتر من مرة. (EF EnsureCreated بيعمل نفس الـ Indexes من الـ Model)

-- ===== Orders =====
-- Q: طلبات عميل معين، الأحدث أولاً   (WHERE CustomerId = ? ORDER BY CreatedAt DESC)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_CustomerId_CreatedAt' AND object_id = OBJECT_ID('dbo.Orders'))
    CREATE INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders (CustomerId ASC, CreatedAt DESC) INCLUDE (Status, Total);

-- Q: Pagination (Offset/Cursor) + تقارير التاريخ  (ORDER BY CreatedAt DESC, Id DESC)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt_Id' AND object_id = OBJECT_ID('dbo.Orders'))
    CREATE INDEX IX_Orders_CreatedAt_Id ON dbo.Orders (CreatedAt DESC, Id DESC) INCLUDE (CustomerId, Status, Total);

-- Q: الطلبات النشطة (Pending=0, Processing=1) ~3% من الجدول  => Filtered Index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_Active_CreatedAt' AND object_id = OBJECT_ID('dbo.Orders'))
    CREATE INDEX IX_Orders_Active_CreatedAt ON dbo.Orders (CreatedAt DESC, Id DESC) INCLUDE (CustomerId, Status, Total) WHERE Status IN (0,1);

-- ===== OrderItems =====
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderItems_OrderId' AND object_id = OBJECT_ID('dbo.OrderItems'))
    CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems (OrderId) INCLUDE (ProductId, Quantity, UnitPrice);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderItems_ProductId' AND object_id = OBJECT_ID('dbo.OrderItems'))
    CREATE INDEX IX_OrderItems_ProductId ON dbo.OrderItems (ProductId) INCLUDE (OrderId, Quantity, UnitPrice);

-- ===== Payments / Reviews / Products =====
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_OrderId' AND object_id = OBJECT_ID('dbo.Payments'))
    CREATE INDEX IX_Payments_OrderId ON dbo.Payments (OrderId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_ProductId_CreatedAt' AND object_id = OBJECT_ID('dbo.Reviews'))
    CREATE INDEX IX_Reviews_ProductId_CreatedAt ON dbo.Reviews (ProductId ASC, CreatedAt DESC) INCLUDE (Rating);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_CustomerId' AND object_id = OBJECT_ID('dbo.Reviews'))
    CREATE INDEX IX_Reviews_CustomerId ON dbo.Reviews (CustomerId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_CategoryId_IsActive' AND object_id = OBJECT_ID('dbo.Products'))
    CREATE INDEX IX_Products_CategoryId_IsActive ON dbo.Products (CategoryId, IsActive) INCLUDE (Price, Name);
