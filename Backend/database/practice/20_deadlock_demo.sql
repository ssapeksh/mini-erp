/* =====================================================================
   20 - Deadlock: reproduce error 1205 and fix it                @manual
   Needs TWO query windows. run-all.ps1 skips this file.

   A deadlock = two transactions each hold a lock the other needs.
   SQL Server picks a victim, rolls it back and raises error 1205.
   ===================================================================== */

/* Setup (run once in either window) */
USE PracticeDb;
DROP TABLE IF EXISTS dbo.AccountA, dbo.AccountB;
CREATE TABLE dbo.AccountA (Id INT PRIMARY KEY, Balance DECIMAL(12,2));
CREATE TABLE dbo.AccountB (Id INT PRIMARY KEY, Balance DECIMAL(12,2));
INSERT INTO dbo.AccountA VALUES (1, 1000);
INSERT INTO dbo.AccountB VALUES (1, 1000);
GO

/* ====================== reproduce =========================================
-- WINDOW 1                                   -- WINDOW 2
BEGIN TRAN;
UPDATE dbo.AccountA SET Balance -= 100;       -- (step 1)
                                              BEGIN TRAN;
                                              UPDATE dbo.AccountB SET Balance -= 50;   -- (step 2)
UPDATE dbo.AccountB SET Balance += 100;       -- (step 3) waits for window 2...
                                              UPDATE dbo.AccountA SET Balance += 50;   -- (step 4) waits for window 1
-- => within ~5 seconds one window gets:
--    Msg 1205: Transaction (Process ID ..) was deadlocked on lock resources with
--    another process and has been chosen as the deadlock victim. Rerun the transaction.
ROLLBACK / COMMIT the survivor.
*/

/* ====================== how to reduce deadlocks ===========================
   1. Access tables in the SAME ORDER everywhere (always A then B). This is why
      usp_DespatchOrder locks stock rows ordered by ItemId.
   2. Keep transactions short: no user input or HTTP calls inside them.
   3. Index the WHERE/JOIN columns so fewer rows are locked.
   4. Use READ COMMITTED SNAPSHOT (ErpDb has it) so readers don't take S locks.
   5. UPDLOCK when you read-then-update the same row.
   6. Retry the victim in code: catch SqlException.Number == 1205
      (EF Core: EnableRetryOnFailure / Polly).

   See recent deadlocks (system_health session keeps deadlock graphs):
   SELECT xed.value('@timestamp', 'datetime2') AS [When], xed.query('.') AS Graph
   FROM (SELECT CAST(target_data AS XML) AS td
         FROM sys.dm_xe_session_targets t
         JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address
         WHERE s.name = 'system_health' AND t.target_name = 'ring_buffer') x
   CROSS APPLY td.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS q(xed);
*/
