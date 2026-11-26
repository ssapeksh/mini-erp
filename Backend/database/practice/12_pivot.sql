/* =====================================================================
   12 - Pivot rows into columns: request status counts per department
   CASE + SUM works everywhere and is easier to explain; PIVOT is the
   SQL Server operator. UNPIVOT turns columns back into rows.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Conditional aggregation (CASE)';
SELECT d.Name AS Dept,
       SUM(CASE WHEN r.Status = 'Approved' THEN 1 ELSE 0 END) AS Approved,
       SUM(CASE WHEN r.Status = 'Rejected' THEN 1 ELSE 0 END) AS Rejected,
       SUM(CASE WHEN r.Status = 'Pending'  THEN 1 ELSE 0 END) AS Pending,
       COUNT(*) AS Total
FROM dbo.Requests r
JOIN dbo.Departments d ON d.Id = r.DeptId
GROUP BY d.Name;

PRINT 'PIVOT operator (same result)';
SELECT Dept, [Approved], [Rejected], [Pending]
FROM (SELECT d.Name AS Dept, r.Status, r.Id
      FROM dbo.Requests r JOIN dbo.Departments d ON d.Id = r.DeptId) AS src
PIVOT (COUNT(Id) FOR Status IN ([Approved], [Rejected], [Pending])) AS p;

PRINT 'Sales by region per quarter of 2025 (pivot on a computed column)';
SELECT Region, [1] AS Q1, [2] AS Q2, [3] AS Q3, [4] AS Q4
FROM (SELECT Region, DATEPART(QUARTER, SaleDate) AS Qtr, Amount
      FROM dbo.Sales WHERE YEAR(SaleDate) = 2025) s
PIVOT (SUM(Amount) FOR Qtr IN ([1], [2], [3], [4])) p;

PRINT 'UNPIVOT: columns back to rows';
WITH Wide AS (
    SELECT d.Name AS Dept,
           SUM(CASE WHEN r.Status = 'Approved' THEN 1 ELSE 0 END) AS Approved,
           SUM(CASE WHEN r.Status = 'Rejected' THEN 1 ELSE 0 END) AS Rejected
    FROM dbo.Requests r JOIN dbo.Departments d ON d.Id = r.DeptId
    GROUP BY d.Name
)
SELECT Dept, Status, Cnt
FROM Wide
UNPIVOT (Cnt FOR Status IN (Approved, Rejected)) u;

PRINT 'Real project: ErpDb order counts by status per warehouse';
SELECT w.Code AS Warehouse,
       SUM(CASE WHEN o.Status LIKE 'Pending%' THEN 1 ELSE 0 END) AS Pending,
       SUM(CASE WHEN o.Status = 'Approved'   THEN 1 ELSE 0 END) AS Approved,
       SUM(CASE WHEN o.Status = 'Posted'     THEN 1 ELSE 0 END) AS Posted,
       SUM(CASE WHEN o.Status = 'Rejected'   THEN 1 ELSE 0 END) AS Rejected
FROM ErpDb.dbo.Orders o JOIN ErpDb.dbo.Warehouses w ON w.Id = o.WarehouseId
GROUP BY w.Code;
GO
