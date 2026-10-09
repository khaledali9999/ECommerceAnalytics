-- بيشيل الـ Non-clustered indexes (ماعدا UX_Customers_Email). بنستخدمه قبل Seeding أو لقياس "قبل التحسين".
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_CustomerId_CreatedAt' AND object_id = OBJECT_ID('dbo.Orders'))   DROP INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt_Id' AND object_id = OBJECT_ID('dbo.Orders'))            DROP INDEX IX_Orders_CreatedAt_Id ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_Active_CreatedAt' AND object_id = OBJECT_ID('dbo.Orders'))        DROP INDEX IX_Orders_Active_CreatedAt ON dbo.Orders;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderItems_OrderId' AND object_id = OBJECT_ID('dbo.OrderItems'))         DROP INDEX IX_OrderItems_OrderId ON dbo.OrderItems;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderItems_ProductId' AND object_id = OBJECT_ID('dbo.OrderItems'))       DROP INDEX IX_OrderItems_ProductId ON dbo.OrderItems;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_OrderId' AND object_id = OBJECT_ID('dbo.Payments'))             DROP INDEX IX_Payments_OrderId ON dbo.Payments;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_ProductId_CreatedAt' AND object_id = OBJECT_ID('dbo.Reviews'))   DROP INDEX IX_Reviews_ProductId_CreatedAt ON dbo.Reviews;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_CustomerId' AND object_id = OBJECT_ID('dbo.Reviews'))            DROP INDEX IX_Reviews_CustomerId ON dbo.Reviews;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_CategoryId_IsActive' AND object_id = OBJECT_ID('dbo.Products')) DROP INDEX IX_Products_CategoryId_IsActive ON dbo.Products;
