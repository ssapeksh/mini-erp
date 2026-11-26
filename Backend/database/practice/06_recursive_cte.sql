/* =====================================================================
   06 - Recursive CTE: employee -> manager hierarchy (org chart)
   Anchor member (the CEO) UNION ALL recursive member (direct reports).
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Top-down org chart with level and path';
WITH Org AS (
    SELECT Id, Name, ManagerId, 0 AS Lvl,
           CAST(Name AS NVARCHAR(500)) AS Path
    FROM dbo.Employees
    WHERE ManagerId IS NULL                       -- anchor: the CEO
    UNION ALL
    SELECT e.Id, e.Name, e.ManagerId, o.Lvl + 1,
           CAST(o.Path + N' > ' + e.Name AS NVARCHAR(500))
    FROM dbo.Employees e
    JOIN Org o ON e.ManagerId = o.Id              -- recursive: people who report to the previous level
)
SELECT REPLICATE(N'    ', Lvl) + Name AS OrgChart, Lvl, Path
FROM Org
ORDER BY Path
OPTION (MAXRECURSION 100);                        -- default is 100; protects against cycles in bad data

PRINT 'Bottom-up: the management chain above Amit (Id 7)';
WITH Chain AS (
    SELECT Id, Name, ManagerId, 0 AS StepsUp FROM dbo.Employees WHERE Id = 7
    UNION ALL
    SELECT m.Id, m.Name, m.ManagerId, c.StepsUp + 1
    FROM dbo.Employees m
    JOIN Chain c ON m.Id = c.ManagerId
)
SELECT StepsUp, Name FROM Chain ORDER BY StepsUp;

PRINT 'Headcount under each manager (all levels, not just direct reports)';
WITH Tree AS (
    SELECT Id AS RootId, Id FROM dbo.Employees
    UNION ALL
    SELECT t.RootId, e.Id FROM dbo.Employees e JOIN Tree t ON e.ManagerId = t.Id
)
SELECT m.Name, COUNT(*) - 1 AS TotalReports
FROM Tree t JOIN dbo.Employees m ON m.Id = t.RootId
GROUP BY m.Name
HAVING COUNT(*) > 1
ORDER BY TotalReports DESC;

PRINT 'Same pattern in ErpDb: the user approval hierarchy';
WITH U AS (
    SELECT Id, UserName, ManagerId, 0 AS Lvl FROM ErpDb.dbo.Users WHERE ManagerId IS NULL
    UNION ALL
    SELECT x.Id, x.UserName, x.ManagerId, U.Lvl + 1 FROM ErpDb.dbo.Users x JOIN U ON x.ManagerId = U.Id
)
SELECT REPLICATE(N'  ', Lvl) + UserName AS Hierarchy FROM U ORDER BY Lvl, UserName;
GO
