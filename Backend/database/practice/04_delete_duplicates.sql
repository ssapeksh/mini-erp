/* =====================================================================
   04 - Find and delete duplicates, keep one
   Customers has duplicate e-mails ('shree', 'om', and 'patel' in
   different letter case). Runs inside a transaction and rolls back, so
   the file can be run again and again.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

PRINT 'Find duplicates with GROUP BY ... HAVING';
SELECT LOWER(Email) AS Email, COUNT(*) AS Copies
FROM dbo.Customers
GROUP BY LOWER(Email)          -- the default collation is case-insensitive anyway; LOWER makes it explicit
HAVING COUNT(*) > 1;

BEGIN TRANSACTION;

PRINT 'Delete all but the OLDEST row per e-mail (deleting through a CTE deletes from the base table)';
WITH Dups AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY LOWER(Email) ORDER BY CreatedOn, Id) AS rn
    FROM dbo.Customers
)
DELETE FROM Dups WHERE rn > 1;

PRINT CONCAT('Rows deleted: ', @@ROWCOUNT);
SELECT Id, Name, Email FROM dbo.Customers ORDER BY Id;

ROLLBACK TRANSACTION;           -- keep the demo data
PRINT 'Rolled back - duplicates restored for the next run.';

/* Prevent it next time: a unique index. (Would fail now because duplicates exist.)
   CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers (Email); */
GO
