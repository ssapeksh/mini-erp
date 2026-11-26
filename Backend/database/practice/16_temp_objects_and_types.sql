/* =====================================================================
   16 - Temp table vs table variable vs CTE; CHAR / VARCHAR / NVARCHAR
   #temp      : in tempdb, has statistics + indexes, visible to nested procs,
                good for large intermediate results
   @table var : in tempdb too, no column statistics (estimates can be poor),
                scoped to the batch, survives ROLLBACK
   CTE        : not stored at all; a named subquery for ONE statement
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

/* #temp table with an index */
DROP TABLE IF EXISTS #RegionTotals;
SELECT Region, SUM(Amount) AS Total
INTO #RegionTotals
FROM dbo.Sales
GROUP BY Region;
CREATE CLUSTERED INDEX CX_RegionTotals ON #RegionTotals (Region);
SELECT * FROM #RegionTotals;

/* Table variable */
DECLARE @Top TABLE (CustomerId INT PRIMARY KEY, Total DECIMAL(14,2));
INSERT INTO @Top
SELECT TOP (3) CustomerId, SUM(Amount) FROM dbo.Sales GROUP BY CustomerId ORDER BY SUM(Amount) DESC;
SELECT * FROM @Top;

/* Interesting difference: a table variable ignores ROLLBACK */
BEGIN TRANSACTION;
    INSERT INTO @Top VALUES (99, 1);
    INSERT INTO #RegionTotals VALUES ('East', 1);
ROLLBACK TRANSACTION;
SELECT (SELECT COUNT(*) FROM @Top)          AS TableVarRows,  -- 4: still there
       (SELECT COUNT(*) FROM #RegionTotals) AS TempTableRows; -- 3: rolled back

/* CTE: lives only for the next statement */
WITH Big AS (SELECT * FROM dbo.Sales WHERE Amount > 45000)
SELECT COUNT(*) AS BigSales FROM Big;
-- SELECT * FROM Big;   -- would fail: the CTE is gone

DROP TABLE #RegionTotals;

/* ---- CHAR vs VARCHAR vs NVARCHAR ---- */
PRINT 'Storage: CHAR pads to its length; VARCHAR stores what you give; NVARCHAR uses 2 bytes per character';
DECLARE @c CHAR(10) = 'abc', @v VARCHAR(10) = 'abc', @n NVARCHAR(10) = N'abc', @hindi NVARCHAR(20) = N'तांबे की तार';
SELECT DATALENGTH(@c) AS CharBytes,       -- 10
       DATALENGTH(@v) AS VarcharBytes,    -- 3
       DATALENGTH(@n) AS NvarcharBytes,   -- 6
       @hindi         AS Hindi,           -- stored correctly
       CAST(@hindi AS VARCHAR(20)) AS HindiInVarchar;   -- '?????' : data lost without NVARCHAR (and the N'' prefix)
GO
