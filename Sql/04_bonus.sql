/* ===================== BONUS 2: Temporal Table على Orders ===================== */
ALTER TABLE dbo.Orders ADD
    SysStart DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN CONSTRAINT DF_Orders_SysStart DEFAULT SYSUTCDATETIME(),
    SysEnd   DATETIME2 GENERATED ALWAYS AS ROW END   HIDDEN CONSTRAINT DF_Orders_SysEnd   DEFAULT CONVERT(DATETIME2,'9999-12-31 23:59:59.9999999'),
    PERIOD FOR SYSTEM_TIME (SysStart, SysEnd);
ALTER TABLE dbo.Orders SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.OrdersHistory));
-- Index للاستعلامات التاريخية
CREATE INDEX IX_OrdersHistory_Id_Period ON dbo.OrdersHistory (Id, SysEnd, SysStart);
-- استعلام: الطلب كان شكله إيه من ساعة؟
-- SELECT * FROM dbo.Orders FOR SYSTEM_TIME AS OF DATEADD(HOUR,-1,SYSUTCDATETIME()) WHERE Id = 123;

/* ===================== BONUS 3: Partitioning (شهري) =====================
   ملاحظة: الـ Partition key لازم يكون ضمن الـ Clustered key، فبنغيّر الـ PK إلى (CreatedAt, Id).
   عملية تتم على Orders فاضية/Staging ثم تنقل البيانات (مش على Production مباشرة).      */
-- CREATE PARTITION FUNCTION pf_OrdersMonthly (DATETIME2) AS RANGE RIGHT FOR VALUES ('2025-01-01','2025-02-01', /* ... لحد الشهر الحالي ... */ '2027-01-01');
-- CREATE PARTITION SCHEME ps_OrdersMonthly AS PARTITION pf_OrdersMonthly ALL TO ([PRIMARY]);
-- CREATE TABLE dbo.Orders_P (... نفس الأعمدة ..., CONSTRAINT PK_Orders_P PRIMARY KEY CLUSTERED (CreatedAt, Id) ON ps_OrdersMonthly(CreatedAt));
-- INSERT INTO dbo.Orders_P SELECT ... FROM dbo.Orders;  -- ثم sp_rename
-- تأكيد Partition Elimination: Actual Execution Plan -> Actual Partition Count = 1

/* ===================== BONUS 4: Query Store - أبطأ الاستعلامات ===================== */
SELECT TOP (20)
    qt.query_sql_text,
    rs.avg_duration / 1000.0       AS avg_ms,
    rs.avg_logical_io_reads        AS avg_reads,
    rs.count_executions
FROM sys.query_store_query_text qt
JOIN sys.query_store_query q          ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p           ON p.query_id = q.query_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
WHERE rs.avg_duration > 500 * 1000     -- > 500ms
ORDER BY rs.avg_duration DESC;

-- اقتراحات Indexes ناقصة (DMV)
SELECT TOP (20)
    d.statement AS table_name, d.equality_columns, d.inequality_columns, d.included_columns,
    s.user_seeks, s.avg_total_user_cost * s.avg_user_impact * (s.user_seeks + s.user_scans) AS improvement_score
FROM sys.dm_db_missing_index_details d
JOIN sys.dm_db_missing_index_groups g ON g.index_handle = d.index_handle
JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle = g.index_group_handle
ORDER BY improvement_score DESC;
