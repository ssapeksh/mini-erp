/* =====================================================================
   08 - Anti-join: departments with no employees (answer: Legal)
   Three ways; NOT EXISTS is usually the best default.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT '1) LEFT JOIN ... WHERE right side IS NULL';
SELECT d.Name
FROM dbo.Departments d
LEFT JOIN dbo.Employees e ON e.DeptId = d.Id
WHERE e.Id IS NULL;

PRINT '2) NOT EXISTS (stops at the first match, NULL-safe)';
SELECT d.Name
FROM dbo.Departments d
WHERE NOT EXISTS (SELECT 1 FROM dbo.Employees e WHERE e.DeptId = d.Id);

PRINT '3) EXCEPT (set difference)';
SELECT Id FROM dbo.Departments
EXCEPT
SELECT DeptId FROM dbo.Employees;

PRINT 'Real project: ErpDb customers who never ordered (C008)';
SELECT c.Code, c.Name
FROM ErpDb.dbo.Customers c
WHERE NOT EXISTS (SELECT 1 FROM ErpDb.dbo.Orders o WHERE o.CustomerId = c.Id);
GO
