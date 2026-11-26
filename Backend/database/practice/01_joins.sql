/* =====================================================================
   01 - JOINS: INNER, LEFT, RIGHT, FULL, CROSS, SELF
   Q: "Draw what each join returns."
   Data traps: the CEO has no department; Legal has no employees.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'INNER JOIN - only matching rows: CEO (no dept) and Legal (no staff) both disappear';
SELECT e.Name, d.Name AS Dept
FROM dbo.Employees e
INNER JOIN dbo.Departments d ON d.Id = e.DeptId;

PRINT 'LEFT JOIN - all employees; CEO appears with Dept = NULL';
SELECT e.Name, d.Name AS Dept
FROM dbo.Employees e
LEFT JOIN dbo.Departments d ON d.Id = e.DeptId;

PRINT 'RIGHT JOIN - all departments; Legal appears with Name = NULL (same as LEFT JOIN with tables swapped)';
SELECT e.Name, d.Name AS Dept
FROM dbo.Employees e
RIGHT JOIN dbo.Departments d ON d.Id = e.DeptId;

PRINT 'FULL OUTER JOIN - everything from both sides: CEO and Legal both appear';
SELECT e.Name, d.Name AS Dept
FROM dbo.Employees e
FULL OUTER JOIN dbo.Departments d ON d.Id = e.DeptId
WHERE e.Id IS NULL OR d.Id IS NULL;           -- just the unmatched rows from either side

PRINT 'CROSS JOIN - every combination (5 depts x 3 statuses = 15 rows). ErpDb seed uses it: every item in every warehouse';
SELECT d.Name, s.Status
FROM dbo.Departments d
CROSS JOIN (VALUES ('Approved'), ('Rejected'), ('Pending')) AS s(Status);

PRINT 'SELF JOIN - a table joined to itself: employee with manager name';
SELECT e.Name AS Employee, m.Name AS Manager
FROM dbo.Employees e
LEFT JOIN dbo.Employees m ON m.Id = e.ManagerId     -- LEFT so the CEO (no manager) stays
ORDER BY m.Name, e.Name;

/* Trap: a filter on the RIGHT table in WHERE turns a LEFT JOIN into an INNER JOIN.
   Put it in ON to keep unmatched left rows. */
PRINT 'LEFT JOIN with filter in ON (keeps all departments) vs in WHERE (drops them)';
SELECT d.Name, COUNT(e.Id) AS HighEarners
FROM dbo.Departments d
LEFT JOIN dbo.Employees e ON e.DeptId = d.Id AND e.Salary > 125000
GROUP BY d.Name;                                     -- 5 rows, zeros included

SELECT d.Name, COUNT(e.Id) AS HighEarners
FROM dbo.Departments d
LEFT JOIN dbo.Employees e ON e.DeptId = d.Id
WHERE e.Salary > 125000                              -- NULL > 125000 is not true -> rows lost
GROUP BY d.Name;                                     -- only departments that have a high earner
GO
