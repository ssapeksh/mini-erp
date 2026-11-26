/* =====================================================================
   04_views_triggers_merge.sql

   Interview topics shown here
   - View (a saved query) vs indexed view (results stored + maintained)
   - Trigger with the inserted / deleted pseudo-tables, writing JSON audit
     rows. Audit logging is the "valid" use of a trigger; business logic in
     triggers is hidden and hard to debug, so it lives in procs/C# instead.
   - SESSION_CONTEXT: lets the trigger know the *application* user even
     though every API call uses the same SQL login (connection pooling)
   - MERGE with OUTPUT $action: insert / update in one statement
   - Temporal-table queries: FOR SYSTEM_TIME (see practice/17)
   ===================================================================== */
SET NOCOUNT ON;
USE ErpDb;
GO

/* ---------------------------------------------------------------------
   VIEWS
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_StockSummary
AS
SELECT i.Id AS ItemId, i.Code, i.Name, c.Name AS Category,
       w.Id AS WarehouseId, w.Code AS Warehouse,
       sb.Quantity, i.Unit, i.ReorderLevel,
       CASE WHEN sb.Quantity <= i.ReorderLevel       THEN 'Reorder'
            WHEN sb.Quantity <= i.ReorderLevel * 1.5 THEN 'Low'
            ELSE 'OK' END AS StockStatus,
       CAST(sb.Quantity * i.UnitPrice AS DECIMAL(18,2)) AS StockValue
FROM dbo.StockBalances sb
JOIN dbo.Items i          ON i.Id = sb.ItemId
JOIN dbo.ItemCategories c ON c.Id = i.CategoryId
JOIN dbo.Warehouses w     ON w.Id = sb.WarehouseId
WHERE i.IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_OrderSummary
AS
SELECT o.Id, o.OrderNo, o.OrderDate, o.Status,
       o.CustomerId, c.Code AS CustomerCode, c.Name AS CustomerName,
       o.WarehouseId, w.Code AS Warehouse,
       o.SubTotal, o.TaxAmount, o.TotalAmount,
       (SELECT COUNT(*) FROM dbo.OrderLines ol WHERE ol.OrderId = o.Id) AS LineCount,   -- correlated subquery
       o.CreatedBy, o.CreatedOn
FROM dbo.Orders o
JOIN dbo.Customers c  ON c.Id = o.CustomerId
JOIN dbo.Warehouses w ON w.Id = o.WarehouseId
WHERE o.IsDeleted = 0;
GO

/* INDEXED VIEW: the unique clustered index materialises the result, and SQL
   Server keeps it up to date on every OrderLines change. Reads are instant;
   writes to OrderLines pay a little extra.
   Rules: WITH SCHEMABINDING, two-part names, COUNT_BIG(*) with GROUP BY,
   no outer joins / subqueries, SUM only over non-nullable expressions. */
CREATE OR ALTER VIEW dbo.vw_ItemSalesTotals
WITH SCHEMABINDING
AS
SELECT ol.ItemId,
       COUNT_BIG(*)               AS LineCount,
       SUM(ol.Qty)                AS TotalQty,
       SUM(ol.Qty * ol.UnitPrice) AS GrossAmount
FROM dbo.OrderLines ol
GROUP BY ol.ItemId;
GO
CREATE UNIQUE CLUSTERED INDEX UCX_vw_ItemSalesTotals ON dbo.vw_ItemSalesTotals (ItemId);
GO

/* ---------------------------------------------------------------------
   AUDIT TRIGGER on Items
   inserted = new row images, deleted = old row images.
   INSERT -> only inserted; DELETE -> only deleted; UPDATE -> both.
   A FULL OUTER JOIN on the key handles all three in one statement, and it
   is set-based: it works when one statement changes many rows.
   --------------------------------------------------------------------- */
CREATE OR ALTER TRIGGER dbo.trg_Items_Audit
ON dbo.Items
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;                                    -- statement touched 0 rows

    -- The API calls sp_set_session_context @key = N'UserName' on each connection.
    DECLARE @ChangedBy NVARCHAR(100) =
        COALESCE(CAST(SESSION_CONTEXT(N'UserName') AS NVARCHAR(100)), SUSER_SNAME());

    INSERT INTO dbo.AuditLog (TableName, RecordId, Action, OldValues, NewValues, ChangedBy)
    SELECT N'Items',
           CAST(COALESCE(i.Id, d.Id) AS NVARCHAR(50)),
           CASE WHEN i.Id IS NOT NULL AND d.Id IS NOT NULL THEN 'U'
                WHEN i.Id IS NOT NULL THEN 'I'
                ELSE 'D' END,
           CASE WHEN d.Id IS NOT NULL THEN
               (SELECT d.Code, d.Name, d.CategoryId, d.UnitPrice, d.TaxPercent, d.ReorderLevel, d.IsActive, d.IsDeleted
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) END,
           CASE WHEN i.Id IS NOT NULL THEN
               (SELECT i.Code, i.Name, i.CategoryId, i.UnitPrice, i.TaxPercent, i.ReorderLevel, i.IsActive, i.IsDeleted
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) END,
           @ChangedBy
    FROM inserted i
    FULL OUTER JOIN deleted d ON d.Id = i.Id
    -- skip UPDATEs that changed nothing we care about (e.g. only ModifiedOn)
    WHERE NOT (i.Id IS NOT NULL AND d.Id IS NOT NULL
               AND i.Code = d.Code AND i.Name = d.Name AND i.CategoryId = d.CategoryId
               AND i.UnitPrice = d.UnitPrice AND i.TaxPercent = d.TaxPercent
               AND i.ReorderLevel = d.ReorderLevel AND i.IsActive = d.IsActive AND i.IsDeleted = d.IsDeleted);
END;
GO

/* ---------------------------------------------------------------------
   MERGE: sync the supplier price file (ItemPriceImport) into Items
   WHEN MATCHED AND changed  -> UPDATE the price (the temporal table keeps the old one)
   WHEN NOT MATCHED BY TARGET -> INSERT a new item
   Rows that match with the same price are left alone, so running the
   proc twice is harmless (idempotent).
   HOLDLOCK on the target avoids the MERGE race where two sessions both
   decide "not matched" and both insert.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_SyncItemPrices
    @ChangedBy NVARCHAR(100) = N'price-import'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Changes TABLE (MergeAction NVARCHAR(10), Code VARCHAR(30), OldPrice DECIMAL(18,2), NewPrice DECIMAL(18,2));

    BEGIN TRANSACTION;

    MERGE dbo.Items WITH (HOLDLOCK) AS tgt
    USING (SELECT s.Code, s.Name, c.Id AS CategoryId, s.UnitPrice
           FROM dbo.ItemPriceImport s
           JOIN dbo.ItemCategories c ON c.Name = s.Category) AS src
       ON tgt.Code = src.Code
    WHEN MATCHED AND tgt.UnitPrice <> src.UnitPrice THEN
        UPDATE SET tgt.UnitPrice = src.UnitPrice,
                   tgt.ModifiedBy = @ChangedBy,
                   tgt.ModifiedOn = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (Code, Name, CategoryId, UnitPrice, CreatedBy)
        VALUES (src.Code, src.Name, src.CategoryId, src.UnitPrice, @ChangedBy)
    OUTPUT $action, inserted.Code, deleted.UnitPrice, inserted.UnitPrice
      INTO @Changes (MergeAction, Code, OldPrice, NewPrice);
    -- (WHEN NOT MATCHED BY SOURCE THEN UPDATE SET IsActive = 0 would retire
    --  items missing from the file; left out on purpose: partial files.)

    COMMIT TRANSACTION;

    SELECT MergeAction, Code, OldPrice, NewPrice FROM @Changes ORDER BY Code;
END;
GO

PRINT 'Views, trigger and MERGE procedure created.';
GO
