/* =====================================================================
   17 - CROSS/OUTER APPLY, MERGE, temporal tables, SESSION_CONTEXT
   Runs against ErpDb objects from 03/04.
   ===================================================================== */
USE ErpDb;
SET NOCOUNT ON;

PRINT 'OUTER APPLY: latest 2 orders per customer, customers without orders kept (C008)';
EXEC dbo.usp_GetLatestOrdersPerCustomer @TopN = 2, @IncludeNoOrders = 1;

PRINT 'CROSS APPLY: same, but customers without orders disappear';
EXEC dbo.usp_GetLatestOrdersPerCustomer @TopN = 2, @IncludeNoOrders = 0;

PRINT 'MERGE run 1: applies the supplier price file';
/* SESSION_CONTEXT: tell the audit trigger who the application user is */
EXEC sp_set_session_context @key = N'UserName', @value = N'manager1';
EXEC dbo.usp_SyncItemPrices @ChangedBy = N'manager1';

PRINT 'MERGE run 2: nothing to do (idempotent)';
EXEC dbo.usp_SyncItemPrices @ChangedBy = N'manager1';

PRINT 'Audit trigger rows (ChangedBy comes from SESSION_CONTEXT)';
SELECT TOP (5) TableName, RecordId, Action, ChangedBy, OldValues, NewValues
FROM dbo.AuditLog ORDER BY Id DESC;

PRINT 'Temporal table: full price history of ELE-001 (current row + ItemsHistory)';
SELECT Code, UnitPrice, ValidFrom, ValidTo
FROM dbo.Items FOR SYSTEM_TIME ALL
WHERE Code = 'ELE-001'
ORDER BY ValidFrom;

PRINT 'Temporal table: what did the price list look like 1 day ago?';
DECLARE @AsOf DATETIME2(3) = DATEADD(DAY, -1, SYSUTCDATETIME());
SELECT Code, UnitPrice
FROM dbo.Items FOR SYSTEM_TIME AS OF @AsOf
WHERE Code IN ('ELE-001', 'PLM-003', 'ELE-006');    -- ELE-006 did not exist yet -> no row
GO
