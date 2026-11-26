/* =====================================================================
   02 - WHERE vs HAVING, logical query order, aggregates and NULL
   Logical order: FROM -> JOIN -> WHERE -> GROUP BY -> HAVING -> SELECT
                  -> DISTINCT -> ORDER BY -> OFFSET/TOP
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'WHERE filters ROWS before grouping; HAVING filters GROUPS after';
SELECT d.Name AS Dept, COUNT(*) AS Staff, AVG(e.Salary) AS AvgSalary
FROM dbo.Employees e
JOIN dbo.Departments d ON d.Id = e.DeptId
WHERE e.HireDate >= '2017-01-01'          -- row filter (can use an index on HireDate)
GROUP BY d.Name
HAVING COUNT(*) >= 3                      -- group filter (aggregate)
ORDER BY AvgSalary DESC;                  -- ORDER BY runs after SELECT, so the alias works here

/* Why can't WHERE use a SELECT alias? WHERE runs before SELECT, so the alias
   doesn't exist yet. The following line would fail with "Invalid column name":
     SELECT Salary * 12 AS Annual FROM dbo.Employees WHERE Annual > 1000000;
   Fix: repeat the expression, or use a CTE / CROSS APPLY: */
PRINT 'Alias reuse with CROSS APPLY';
SELECT e.Name, a.Annual
FROM dbo.Employees e
CROSS APPLY (SELECT e.Salary * 12 AS Annual) a
WHERE a.Annual > 1800000;

PRINT 'Aggregates and NULL: Ishaan has NULL salary';
SELECT COUNT(*)              AS AllRows,          -- 16: counts rows
       COUNT(Salary)         AS WithSalary,       -- 15: ignores NULL
       SUM(Salary)           AS Total,            -- ignores NULL
       AVG(Salary)           AS AvgIgnoringNull,  -- divides by 15
       AVG(ISNULL(Salary,0)) AS AvgNullAsZero,    -- divides by 16 -> lower
       COUNT(DISTINCT Salary) AS DistinctSalaries
FROM dbo.Employees;

PRINT 'COALESCE vs ISNULL';
SELECT Name, ISNULL(Salary, 0) AS IsNullVersion,   -- T-SQL only, 2 arguments, type of the first arg
       COALESCE(Salary, 0)    AS CoalesceVersion   -- ANSI, many arguments
FROM dbo.Employees WHERE Salary IS NULL;           -- '= NULL' never matches: always use IS NULL
GO
