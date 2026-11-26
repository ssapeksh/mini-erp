/* =====================================================================
   03 - Nth highest salary; ROW_NUMBER vs RANK vs DENSE_RANK
   ROW_NUMBER 1,2,3,4 | RANK 1,2,2,4 | DENSE_RANK 1,2,2,3
   Data: Rohan and Sneha both earn 120000 (a tie).
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'The three ranking functions side by side (Engineering)';
SELECT Name, Salary,
       ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RowNum,      -- unique, ties broken arbitrarily
       RANK()       OVER (ORDER BY Salary DESC) AS Rnk,         -- ties share, then a gap
       DENSE_RANK() OVER (ORDER BY Salary DESC) AS DenseRnk     -- ties share, no gap
FROM dbo.Employees
WHERE DeptId = 1 AND Salary IS NOT NULL;

PRINT 'Nth highest salary (N = 2) with DENSE_RANK - correct even with ties';
DECLARE @N INT = 2;
WITH Ranked AS (
    SELECT Name, Salary, DENSE_RANK() OVER (ORDER BY Salary DESC) AS Rnk
    FROM dbo.Employees
    WHERE Salary IS NOT NULL
)
SELECT Name, Salary FROM Ranked WHERE Rnk = @N;

PRINT 'Second highest DISTINCT salary without window functions (older style)';
SELECT MAX(Salary) AS SecondHighest
FROM dbo.Employees
WHERE Salary < (SELECT MAX(Salary) FROM dbo.Employees);

PRINT 'Nth highest with OFFSET/FETCH over DISTINCT salaries';
SELECT Salary
FROM (SELECT DISTINCT Salary FROM dbo.Employees WHERE Salary IS NOT NULL) s
ORDER BY Salary DESC
OFFSET @N - 1 ROWS FETCH NEXT 1 ROW ONLY;
GO
