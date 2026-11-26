/* =====================================================================
   tuning-lab.sql  -  "How do you tune a slow query?" with real numbers

   The guide's 6 steps, each demonstrated below:
     1. Read the actual execution plan (SSMS: Ctrl+M, then run)
     2. Add/adjust indexes - covering indexes on WHERE / JOIN columns
     3. Make predicates SARGable (no functions on indexed columns)
     4. Select only the columns you need (never SELECT *)
     5. Check for parameter sniffing in stored procedures
     6. Measure before/after: SET STATISTICS IO, TIME ON

   Each test records the LOGICAL READS (8 KB pages read from memory) used by
   the statement, so the summary at the end shows before vs after side by side.
   Takes ~10-20 seconds (builds a 200,000-row table).
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;
GO

/* ---------- build the lab table ---------- */
DROP TABLE IF EXISTS dbo.BigOrders;
DROP TABLE IF EXISTS dbo.LabResults;
CREATE TABLE dbo.LabResults (Step INT IDENTITY(1,1), Test VARCHAR(80), LogicalReads BIGINT, Note VARCHAR(120));

CREATE TABLE dbo.BigOrders
(
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BigOrders PRIMARY KEY CLUSTERED,
    CustomerId INT           NOT NULL,
    OrderDate  DATETIME2(0)  NOT NULL,
    Status     VARCHAR(20)   NOT NULL,
    Amount     DECIMAL(12,2) NOT NULL,
    Notes      CHAR(200)     NOT NULL      -- wide filler column: makes scans expensive, like real rows
);

;WITH N AS (
    SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.BigOrders WITH (TABLOCK) (CustomerId, OrderDate, Status, Amount, Notes)
SELECT n % 5000 + 1,
       DATEADD(MINUTE, n * 7, '2023-01-01'),
       CASE WHEN n % 1000 = 0 THEN 'Pending' ELSE 'Posted' END,  -- skewed: 0.1% Pending, 99.9% Posted
       (n * 37) % 100000 / 10.0,
       'x'
FROM N;
PRINT 'BigOrders: 200,000 rows created.';
GO

/* Helper: logical reads of the CURRENT request (sys.dm_exec_requests counts live;
   sys.dm_exec_sessions only updates when a batch ends) */
CREATE OR ALTER PROCEDURE dbo.usp_LabMark @Test VARCHAR(80), @Note VARCHAR(120), @Start BIGINT
AS
    INSERT INTO dbo.LabResults (Test, LogicalReads, Note)
    SELECT @Test, logical_reads - @Start, @Note FROM sys.dm_exec_requests WHERE session_id = @@SPID;
GO

/* =====================================================================
   STEP 3 - SARGable predicates (Search ARGument ABLE = can seek an index)
   ===================================================================== */
CREATE NONCLUSTERED INDEX IX_BigOrders_OrderDate ON dbo.BigOrders (OrderDate);

DECLARE @r BIGINT, @cnt INT;

-- the guide's own syntax for step 6: messages tab shows "logical reads" per table
SET STATISTICS IO, TIME ON;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @cnt = COUNT(*) FROM dbo.BigOrders WHERE YEAR(OrderDate) = 2024;        -- function on the column
EXEC dbo.usp_LabMark 'SARG: WHERE YEAR(OrderDate) = 2024', 'index SCAN: YEAR() must run on every row', @r;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @cnt = COUNT(*) FROM dbo.BigOrders
WHERE OrderDate >= '2024-01-01' AND OrderDate < '2025-01-01';                   -- same rows, as a range
EXEC dbo.usp_LabMark 'SARG: OrderDate >= 2024-01-01 AND < 2025-01-01', 'index SEEK on the range', @r;

SET STATISTICS IO, TIME OFF;
GO

/* Implicit conversion: N'...' is NVARCHAR, the column is VARCHAR.
   NVARCHAR has higher precedence, so SQL Server converts the COLUMN on every
   row (CONVERT_IMPLICIT in the plan). Typical cause: .NET string parameters
   sent as nvarchar to a varchar column. EF Core: map with .IsUnicode(false). */
CREATE NONCLUSTERED INDEX IX_BigOrders_Status ON dbo.BigOrders (Status);
GO   -- own batch: an index hint can't name an index created in the same batch

DECLARE @r BIGINT, @cnt INT;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @cnt = COUNT(*) FROM dbo.BigOrders WITH (INDEX(IX_BigOrders_Status)) WHERE Status = N'Pending';
EXEC dbo.usp_LabMark 'Type mismatch: Status = N''Pending'' (nvarchar)', 'CONVERT_IMPLICIT on the column: scans the index', @r;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @cnt = COUNT(*) FROM dbo.BigOrders WHERE Status = 'Pending';
EXEC dbo.usp_LabMark 'Type match:    Status = ''Pending'' (varchar)', 'clean SEEK', @r;
GO

/* =====================================================================
   STEP 2 + 4 - key lookups, covering indexes, SELECT *
   ===================================================================== */
CREATE NONCLUSTERED INDEX IX_BigOrders_CustomerId ON dbo.BigOrders (CustomerId);
DECLARE @r BIGINT, @x DECIMAL(14,2);

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @x = SUM(Amount) FROM dbo.BigOrders WHERE CustomerId = 42;
EXEC dbo.usp_LabMark 'Lookup: index on CustomerId only', 'SEEK + KEY LOOKUP per row to fetch Amount', @r;

/* covering index: the INCLUDE columns live in the index leaf, no lookups */
CREATE NONCLUSTERED INDEX IX_BigOrders_CustomerId_Covering
    ON dbo.BigOrders (CustomerId) INCLUDE (OrderDate, Amount);

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT @x = SUM(Amount) FROM dbo.BigOrders WHERE CustomerId = 42;
EXEC dbo.usp_LabMark 'Covering: (CustomerId) INCLUDE (OrderDate, Amount)', 'SEEK only', @r;

/* SELECT * needs the wide Notes column, so the covering index can't cover it */
DROP TABLE IF EXISTS #sinkAll, #sinkCols;
SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT * INTO #sinkAll FROM dbo.BigOrders WHERE CustomerId BETWEEN 100 AND 400;
EXEC dbo.usp_LabMark 'SELECT * (CustomerId 100-400)', 'needs every column: lookups or a full scan', @r;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
SELECT Id, OrderDate, Amount INTO #sinkCols FROM dbo.BigOrders WHERE CustomerId BETWEEN 100 AND 400;
EXEC dbo.usp_LabMark 'SELECT Id, OrderDate, Amount (same rows)', 'served from the covering index', @r;
DROP TABLE #sinkAll, #sinkCols;   -- (two SELECT INTO the same #name in one batch is a compile error)
GO

/* =====================================================================
   STEP 5 - parameter sniffing
   The plan is compiled for the FIRST parameter value and then reused.
   'Pending' = 200 rows  -> seek + lookups is a great plan
   'Posted'  = 199,800   -> the same plan does ~200k lookups: terrible
   ===================================================================== */
CREATE OR ALTER PROCEDURE dbo.usp_OrdersByStatus @Status VARCHAR(20)
AS
    SELECT SUM(Amount) AS Total FROM dbo.BigOrders WHERE Status = @Status;
GO
CREATE OR ALTER PROCEDURE dbo.usp_OrdersByStatus_Recompile @Status VARCHAR(20)
AS
    SELECT SUM(Amount) AS Total FROM dbo.BigOrders WHERE Status = @Status
    OPTION (RECOMPILE);         -- fresh plan per call (costs a compile each time)
GO
/* Other fixes: OPTION (OPTIMIZE FOR (@Status = 'Posted')) or
   OPTION (OPTIMIZE FOR UNKNOWN), copying the parameter into a local variable
   (uses average density), or separate procs for the two cases. */

EXEC sp_recompile 'dbo.usp_OrdersByStatus';
DECLARE @r BIGINT;
DECLARE @t TABLE (Total DECIMAL(18,2));

INSERT INTO @t EXEC dbo.usp_OrdersByStatus 'Pending';            -- first call: plan built for 200 rows
SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
INSERT INTO @t EXEC dbo.usp_OrdersByStatus 'Posted';             -- reuses the 'Pending' plan
EXEC dbo.usp_LabMark 'Sniffing: ''Posted'' with plan cached for ''Pending''', 'seek + ~200k key lookups', @r;

SELECT @r = logical_reads FROM sys.dm_exec_requests WHERE session_id = @@SPID;
INSERT INTO @t EXEC dbo.usp_OrdersByStatus_Recompile 'Posted';
EXEC dbo.usp_LabMark 'Sniffing fixed: OPTION (RECOMPILE)', 'right plan for ''Posted'': one scan', @r;
GO

/* =====================================================================
   STATISTICS - what the optimiser knows about data distribution
   ===================================================================== */
PRINT 'Histogram for IX_BigOrders_Status: the optimiser knows Pending is rare';
DBCC SHOW_STATISTICS ('dbo.BigOrders', IX_BigOrders_Status) WITH HISTOGRAM;
UPDATE STATISTICS dbo.BigOrders WITH FULLSCAN;     -- after big data loads; auto-update handles normal traffic
GO

/* =====================================================================
   FRAGMENTATION - random GUID keys cause page splits
   ===================================================================== */
DROP TABLE IF EXISTS dbo.FragDemo;
CREATE TABLE dbo.FragDemo (Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY, Filler CHAR(300) NOT NULL DEFAULT 'x');
/* Many small inserts, like real OLTP traffic. (One big INSERT...SELECT would be
   sorted by key first and NOT fragment the index.) */
DECLARE @i INT = 0;
WHILE @i < 2000
BEGIN
    INSERT INTO dbo.FragDemo (Filler) SELECT TOP (10) 'x' FROM sys.all_objects;
    SET @i += 1;
END;

PRINT 'Fragmentation after 20,000 random-GUID inserts';
SELECT OBJECT_NAME(ps.object_id) AS TableName, i.name AS IndexName,
       CAST(ps.avg_fragmentation_in_percent AS DECIMAL(5,1)) AS FragPct, ps.page_count,
       CASE WHEN ps.avg_fragmentation_in_percent > 30 THEN 'REBUILD'
            WHEN ps.avg_fragmentation_in_percent > 5  THEN 'REORGANIZE'
            ELSE 'OK' END AS Advice
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.FragDemo'), NULL, NULL, 'LIMITED') ps
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id;

ALTER INDEX ALL ON dbo.FragDemo REBUILD;            -- REORGANIZE for 5-30%: online, lighter
PRINT 'After REBUILD:';
SELECT CAST(avg_fragmentation_in_percent AS DECIMAL(5,1)) AS FragPct, page_count
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.FragDemo'), NULL, NULL, 'LIMITED');
/* Prevention: sequential keys (IDENTITY, NEWSEQUENTIALID() as in ErpDb.UserSessions). */
DROP TABLE dbo.FragDemo;
GO

/* ---------- summary ---------- */
PRINT '==================== TUNING LAB RESULTS (logical reads, lower is better) ====================';
SELECT Step, Test, LogicalReads, Note FROM dbo.LabResults ORDER BY Step;
GO
