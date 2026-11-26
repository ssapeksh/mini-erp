/* =====================================================================
   07 - LAG / LEAD: compare a row with the previous / next one
   Q: "Month-wise sales totals with month-over-month change."
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Monthly totals with previous month, next month and % change';
WITH Monthly AS (
    SELECT DATEFROMPARTS(YEAR(SaleDate), MONTH(SaleDate), 1) AS MonthStart,
           SUM(Amount) AS Total
    FROM dbo.Sales
    GROUP BY DATEFROMPARTS(YEAR(SaleDate), MONTH(SaleDate), 1)
    -- grouping by a real DATE (not FORMAT(...,'yyyy-MM')) is cheaper and sorts correctly
)
SELECT FORMAT(MonthStart, 'yyyy-MM')                   AS [Month],
       Total,
       LAG(Total)  OVER (ORDER BY MonthStart)          AS PrevMonth,
       LEAD(Total) OVER (ORDER BY MonthStart)          AS NextMonth,
       Total - LAG(Total) OVER (ORDER BY MonthStart)   AS Change,
       CAST(100.0 * (Total - LAG(Total) OVER (ORDER BY MonthStart))
            / NULLIF(LAG(Total) OVER (ORDER BY MonthStart), 0) AS DECIMAL(8,2)) AS ChangePct,
       LAG(Total, 12) OVER (ORDER BY MonthStart)       AS SameMonthLastYear    -- offset of 12
FROM Monthly
ORDER BY MonthStart;

PRINT 'Days since the same customer''s previous purchase';
SELECT TOP (12) CustomerId, SaleDate,
       LAG(SaleDate) OVER (PARTITION BY CustomerId ORDER BY SaleDate) AS PrevSale,
       DATEDIFF(DAY, LAG(SaleDate) OVER (PARTITION BY CustomerId ORDER BY SaleDate), SaleDate) AS GapDays,
       FIRST_VALUE(SaleDate) OVER (PARTITION BY CustomerId ORDER BY SaleDate) AS FirstSale
FROM dbo.Sales
WHERE CustomerId = 3
ORDER BY SaleDate;

PRINT 'Real project: ErpDb month-over-month sales (LAG + running FY total)';
EXEC ErpDb.dbo.usp_GetMonthlySales;
GO
