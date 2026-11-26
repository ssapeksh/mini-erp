/* =====================================================================
   19 - Isolation levels, dirty reads, NOLOCK, snapshot          @manual
   Needs TWO query windows (SSMS / Azure Data Studio / two sqlcmd sessions)
   connected to localhost,14333. run-all.ps1 skips this file.

   Level              Dirty read  Non-repeatable  Phantom   How
   READ UNCOMMITTED   yes         yes             yes       NOLOCK hint
   READ COMMITTED     no          yes             yes       default (locking)
   REPEATABLE READ    no          no              yes       holds S locks
   SERIALIZABLE       no          no              no        range locks
   SNAPSHOT           no          no              no        row versions in tempdb
   RCSI (ErpDb)       no          yes             yes       READ COMMITTED using versions
   ===================================================================== */

/* ===================== DEMO 1: dirty read with NOLOCK =====================
-- WINDOW A
USE PracticeDb;
BEGIN TRANSACTION;
UPDATE dbo.Employees SET Salary = 999999 WHERE Id = 7;     -- not committed yet

-- WINDOW B
USE PracticeDb;
SELECT Name, Salary FROM dbo.Employees WITH (NOLOCK) WHERE Id = 7;  -- 999999: a DIRTY read
SELECT Name, Salary FROM dbo.Employees WHERE Id = 7;                -- waits (blocked) under READ COMMITTED

-- WINDOW A
ROLLBACK;            -- the 999999 that window B read never existed
   Why NOLOCK is risky: dirty reads, and during page splits it can even
   skip rows or read the same row twice.
*/

/* ===================== DEMO 2: snapshot isolation ========================
-- WINDOW A
USE PracticeDb;
BEGIN TRANSACTION;
UPDATE dbo.Employees SET Salary = 100000 WHERE Id = 7;

-- WINDOW B
USE PracticeDb;
SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
SELECT Name, Salary FROM dbo.Employees WHERE Id = 7;   -- returns the last COMMITTED value at once, no blocking

-- WINDOW A
ROLLBACK;
*/

/* ===================== DEMO 3: blocking chain diagnosis ==================
-- While WINDOW A holds a lock and WINDOW B waits, run in a third window:
SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time, t.text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.blocking_session_id <> 0;
*/

/* Lock types (theory)
   S  shared    - readers; many at once
   X  exclusive - writers; nobody else
   U  update    - "I read this and will write it"; only one U at a time, which
                  prevents the classic conversion deadlock (two S locks both
                  trying to become X). UPDLOCK in usp_DespatchOrder asks for it.
   Lock escalation: many row/page locks (~5000) -> one table lock. */
SELECT 'Open this file and follow the two-window steps in the comments.' AS Instructions;
