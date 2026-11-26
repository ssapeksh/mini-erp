/* =====================================================================
   05 - Running totals and moving averages (window aggregates)
   The real-project version is ErpDb.dbo.usp_GetRunningStock.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Running total per region + 3-row moving average';
SELECT TOP (15) Region, SaleDate, Amount,
       SUM(Amount) OVER (PARTITION BY Region ORDER BY SaleDate, Id
                         ROWS UNBOUNDED PRECEDING)          AS RunningTotal,
       AVG(Amount) OVER (PARTITION BY Region ORDER BY SaleDate, Id
                         ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS MovingAvg3,
       SUM(Amount) OVER (PARTITION BY Region)                AS RegionTotal,   -- no ORDER BY = whole partition
       CAST(100.0 * Amount / SUM(Amount) OVER (PARTITION BY Region) AS DECIMAL(5,2)) AS PctOfRegion
FROM dbo.Sales
WHERE Region = 'West'
ORDER BY Region, SaleDate, Id;

/* ROWS vs RANGE: with ORDER BY and no frame, the default frame is
   RANGE UNBOUNDED PRECEDING, which lumps rows with the SAME SaleDate together
   (they all get the same running total) and uses a slower on-disk spool.
   Always write ROWS UNBOUNDED PRECEDING for running totals. */

PRINT 'Real project: running stock for item 1 in ErpDb';
EXEC ErpDb.dbo.usp_GetRunningStock @ItemId = 1, @WarehouseId = 1;
GO
