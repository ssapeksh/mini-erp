/* =====================================================================
   00_create_databases.sql
   Creates the two databases used in Phase 1:
     ErpDb       - the MiniErp application database
     PracticeDb  - a playground for the interview SQL questions
   Run with: Backend/database/run-all.ps1  (it drops + recreates both)
   ===================================================================== */
SET NOCOUNT ON;
USE master;
GO

IF DB_ID(N'ErpDb') IS NULL
BEGIN
    CREATE DATABASE ErpDb;
    PRINT 'Created ErpDb';
END
GO

/* READ_COMMITTED_SNAPSHOT: readers see the last committed version instead of
   waiting on writers' locks. This removes most reader/writer blocking and
   deadlocks without the dirty-read risk of NOLOCK.  (Guide: SQL Advanced) */
ALTER DATABASE ErpDb SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE ErpDb SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

IF DB_ID(N'PracticeDb') IS NULL
BEGIN
    CREATE DATABASE PracticeDb;
    PRINT 'Created PracticeDb';
END
GO

/* PracticeDb keeps the default (locking) READ COMMITTED so the blocking,
   dirty-read and deadlock demos behave like a classic SQL Server.
   Snapshot isolation is allowed so it can be compared on demand. */
ALTER DATABASE PracticeDb SET ALLOW_SNAPSHOT_ISOLATION ON;
GO
