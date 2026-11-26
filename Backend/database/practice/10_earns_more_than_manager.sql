/* =====================================================================
   10 - Employees who earn more than their manager (self join)
   Expected: Neha (130000 > Rohan 120000), Pooja (170000 > Arjun 160000)
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

SELECT e.Name AS Employee, e.Salary, m.Name AS Manager, m.Salary AS ManagerSalary
FROM dbo.Employees e
JOIN dbo.Employees m ON e.ManagerId = m.Id
WHERE e.Salary > m.Salary;

PRINT 'Follow-up: employees earning more than their department average (correlated subquery)';
SELECT e.Name, e.DeptId, e.Salary
FROM dbo.Employees e
WHERE e.Salary > (SELECT AVG(x.Salary) FROM dbo.Employees x WHERE x.DeptId = e.DeptId);

PRINT 'Same thing with a window function (one pass over the table)';
SELECT Name, DeptId, Salary, DeptAvg
FROM (SELECT Name, DeptId, Salary, AVG(Salary) OVER (PARTITION BY DeptId) AS DeptAvg
      FROM dbo.Employees) t
WHERE Salary > DeptAvg;
GO
