/* =====================================================================
   13 - IN vs EXISTS, and the NOT IN + NULL trap
   Q: "Which employees are NOT managers?"
   ManagerId contains NULL (the CEO), which breaks NOT IN.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'IN and EXISTS give the same answer for positive checks (who IS a manager)';
SELECT Name FROM dbo.Employees WHERE Id IN (SELECT ManagerId FROM dbo.Employees);
SELECT Name FROM dbo.Employees e WHERE EXISTS (SELECT 1 FROM dbo.Employees x WHERE x.ManagerId = e.Id);

PRINT 'TRAP: NOT IN returns ZERO rows because the subquery contains a NULL';
SELECT Name FROM dbo.Employees
WHERE Id NOT IN (SELECT ManagerId FROM dbo.Employees);
/* Why: "Id NOT IN (2, 1, NULL)" means Id<>2 AND Id<>1 AND Id<>NULL.
   Id<>NULL is UNKNOWN, so the whole AND is never TRUE. */

PRINT 'FIX 1: NOT EXISTS (NULL-safe, the recommended form)';
SELECT Name FROM dbo.Employees e
WHERE NOT EXISTS (SELECT 1 FROM dbo.Employees x WHERE x.ManagerId = e.Id);

PRINT 'FIX 2: filter the NULLs out of the NOT IN list';
SELECT Name FROM dbo.Employees
WHERE Id NOT IN (SELECT ManagerId FROM dbo.Employees WHERE ManagerId IS NOT NULL);
GO
