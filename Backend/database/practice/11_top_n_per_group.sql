/* =====================================================================
   11 - Top 3 earners in each department
   DENSE_RANK keeps ties (could return more than 3 rows per dept);
   ROW_NUMBER returns exactly 3. Say which one the business wants.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Top 3 per department with DENSE_RANK (ties included)';
SELECT Dept, Name, Salary, Rnk
FROM (
    SELECT d.Name AS Dept, e.Name, e.Salary,
           DENSE_RANK() OVER (PARTITION BY e.DeptId ORDER BY e.Salary DESC) AS Rnk
    FROM dbo.Employees e
    JOIN dbo.Departments d ON d.Id = e.DeptId
    WHERE e.Salary IS NOT NULL
) t
WHERE Rnk <= 3
ORDER BY Dept, Rnk;

PRINT 'Highest paid per department (exactly one row each, ROW_NUMBER)';
WITH R AS (
    SELECT DeptId, Name, Salary,
           ROW_NUMBER() OVER (PARTITION BY DeptId ORDER BY Salary DESC, HireDate) AS rn
    FROM dbo.Employees
    WHERE DeptId IS NOT NULL
)
SELECT DeptId, Name, Salary FROM R WHERE rn = 1;

PRINT 'Alternative: CROSS APPLY + TOP (also good for "latest N orders per customer")';
SELECT d.Name AS Dept, x.Name, x.Salary
FROM dbo.Departments d
CROSS APPLY (SELECT TOP (2) e.Name, e.Salary
             FROM dbo.Employees e
             WHERE e.DeptId = d.Id
             ORDER BY e.Salary DESC) x;
GO
