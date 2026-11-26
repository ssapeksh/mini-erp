/* =====================================================================
   15 - Statement families, DELETE vs TRUNCATE vs DROP, UNION vs UNION ALL
   DDL: CREATE ALTER DROP TRUNCATE      DML: SELECT INSERT UPDATE DELETE MERGE
   DCL: GRANT REVOKE DENY               TCL: BEGIN TRAN COMMIT ROLLBACK SAVE TRAN
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

/* ---- DDL ---- */
DROP TABLE IF EXISTS dbo.Scratch;
CREATE TABLE dbo.Scratch (Id INT IDENTITY(1,1) PRIMARY KEY, Note NVARCHAR(50));
ALTER TABLE dbo.Scratch ADD CreatedOn DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME();

/* ---- DML ---- */
INSERT INTO dbo.Scratch (Note) VALUES (N'a'), (N'b'), (N'c');
UPDATE dbo.Scratch SET Note = N'B' WHERE Note = N'b';

/* ---- TCL with a savepoint ---- */
BEGIN TRANSACTION;
    INSERT INTO dbo.Scratch (Note) VALUES (N'kept');
    SAVE TRANSACTION BeforeMistake;
    DELETE FROM dbo.Scratch;                       -- oops
    ROLLBACK TRANSACTION BeforeMistake;            -- undo only back to the savepoint
COMMIT TRANSACTION;
SELECT Id, Note FROM dbo.Scratch;                   -- a, B, c, kept

/* ---- DELETE vs TRUNCATE ---- */
DELETE FROM dbo.Scratch WHERE Note = N'kept';      -- row by row, fully logged, can use WHERE, fires triggers
INSERT INTO dbo.Scratch (Note) VALUES (N'd');
SELECT MAX(Id) AS IdAfterDelete FROM dbo.Scratch;  -- identity continues (5)

TRUNCATE TABLE dbo.Scratch;                        -- all rows, deallocates pages, minimal logging,
INSERT INTO dbo.Scratch (Note) VALUES (N'e');      -- no WHERE, no triggers, RESETS identity,
SELECT MAX(Id) AS IdAfterTruncate FROM dbo.Scratch;-- fails if a foreign key references the table  -> 1
-- Both DELETE and TRUNCATE can be rolled back inside a transaction in SQL Server.

/* ---- DCL ---- */
IF DATABASE_PRINCIPAL_ID('ReportReader') IS NULL CREATE ROLE ReportReader;
GRANT SELECT ON dbo.Sales TO ReportReader;         -- least privilege: read one table
DENY  SELECT ON dbo.Employees TO ReportReader;     -- DENY beats any GRANT
REVOKE SELECT ON dbo.Sales FROM ReportReader;      -- remove the grant again

/* ---- DROP ---- */
DROP TABLE dbo.Scratch;                            -- structure + data gone

/* ---- UNION vs UNION ALL ---- */
PRINT 'UNION removes duplicates (extra sort/hash); UNION ALL keeps them and is faster';
SELECT City FROM dbo.Customers WHERE City = N'Mumbai'
UNION
SELECT City FROM dbo.Customers WHERE City IN (N'Mumbai', N'Pune');   -- Mumbai, Pune

SELECT City FROM dbo.Customers WHERE City = N'Mumbai'
UNION ALL
SELECT City FROM dbo.Customers WHERE City IN (N'Mumbai', N'Pune');   -- duplicates kept
GO
