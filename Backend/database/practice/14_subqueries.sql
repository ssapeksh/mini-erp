/* =====================================================================
   14 - Subqueries: scalar, multi-row, correlated, derived table, CTE
   A correlated subquery references the outer query and (logically) runs
   once per outer row; the optimiser often rewrites it as a join.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Scalar subquery in SELECT: each employee vs company average';
SELECT Name, Salary,
       (SELECT AVG(Salary) FROM dbo.Employees) AS CompanyAvg
FROM dbo.Employees
WHERE Salary IS NOT NULL;

PRINT 'Correlated subquery: number of direct reports per employee';
SELECT e.Name,
       (SELECT COUNT(*) FROM dbo.Employees r WHERE r.ManagerId = e.Id) AS DirectReports
FROM dbo.Employees e
ORDER BY DirectReports DESC;

PRINT 'Derived table (subquery in FROM) vs CTE: identical plans, CTE is easier to read';
SELECT t.DeptId, t.MaxSalary
FROM (SELECT DeptId, MAX(Salary) AS MaxSalary FROM dbo.Employees GROUP BY DeptId) AS t
WHERE t.DeptId IS NOT NULL;

WITH DeptMax AS (SELECT DeptId, MAX(Salary) AS MaxSalary FROM dbo.Employees GROUP BY DeptId)
SELECT DeptId, MaxSalary FROM DeptMax WHERE DeptId IS NOT NULL;

PRINT 'ANY / ALL: earns more than EVERYONE in Finance';
SELECT Name, Salary FROM dbo.Employees
WHERE Salary > ALL (SELECT Salary FROM dbo.Employees WHERE DeptId = 3);
GO
