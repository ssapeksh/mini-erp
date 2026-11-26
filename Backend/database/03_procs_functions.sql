/* =====================================================================
   03_procs_functions.sql  -  stored procedures and functions

   Interview topics shown here
   - Stored procedure vs function (procs change data + many result sets;
     functions return a value/table and can be used inside SELECT)
   - Scalar function vs inline table-valued function (iTVF)
   - Transactions: BEGIN TRAN / COMMIT / ROLLBACK, TRY...CATCH, THROW,
     SET XACT_ABORT ON, @@TRANCOUNT
   - Locking hints UPDLOCK + HOLDLOCK to stop double-despatch races
   - Custom error numbers (50404/50409/50422) that the API maps to HTTP codes
   - Transactional outbox (event saved in the same transaction)
   - Server-side pagination: OFFSET ... FETCH NEXT
   - Window functions: SUM() OVER running total, LAG for month-over-month
   - CROSS APPLY vs OUTER APPLY (latest N orders per customer)
   - Catch-all search + OPTION (RECOMPILE) against parameter sniffing
   - SEQUENCE-based document numbers
   ===================================================================== */
SET NOCOUNT ON;
USE ErpDb;
GO

/* ---------------------------------------------------------------------
   FUNCTIONS
   --------------------------------------------------------------------- */

/* Scalar function: Indian financial year (April-March) for a date.
   SCHEMABINDING + deterministic body lets SQL Server 2019+ inline it. */
CREATE OR ALTER FUNCTION dbo.fn_FinancialYear (@Date DATE)
RETURNS VARCHAR(7)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @StartYear INT = CASE WHEN MONTH(@Date) >= 4 THEN YEAR(@Date) ELSE YEAR(@Date) - 1 END;
    RETURN CONCAT(@StartYear, '-', RIGHT(@StartYear + 1, 2));     -- e.g. 2026-27
END;
GO

/* Scalar function that reads a table: the classic SLOW pattern.
   Used in a SELECT over many rows it runs once PER ROW (hidden RBAR).
   Compare its plan with fn_ItemStock below (see practice/18_functions.sql). */
CREATE OR ALTER FUNCTION dbo.fn_ItemStockScalar (@ItemId INT)
RETURNS DECIMAL(18,3)
AS
BEGIN
    RETURN (SELECT ISNULL(SUM(Quantity), 0) FROM dbo.StockBalances WHERE ItemId = @ItemId);
END;
GO

/* Inline table-valued function: a single SELECT, expanded into the calling
   query like a parameterised view, so the optimiser can join it properly. */
CREATE OR ALTER FUNCTION dbo.fn_ItemStock (@ItemId INT)
RETURNS TABLE
AS
RETURN
    SELECT w.Id AS WarehouseId, w.Code AS WarehouseCode, w.Name AS WarehouseName,
           ISNULL(sb.Quantity, 0) AS Quantity
    FROM dbo.Warehouses w
    LEFT JOIN dbo.StockBalances sb ON sb.WarehouseId = w.Id AND sb.ItemId = @ItemId
    WHERE w.IsDeleted = 0;
GO

/* ---------------------------------------------------------------------
   DOCUMENT NUMBERS
   A SEQUENCE hands out numbers without locking a counter table, so two
   users can never get the same number. Note: a rolled-back transaction
   leaves a gap (fine for sales orders; tax invoices that must be gapless
   need a locked counter row instead).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetNextOrderNo
    @OrderDate DATE,
    @OrderNo   VARCHAR(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Next INT = NEXT VALUE FOR dbo.OrderNoSeq;
    SET @OrderNo = CONCAT('SO/', dbo.fn_FinancialYear(@OrderDate), '/', FORMAT(@Next, '000000'));
END;
GO

/* ---------------------------------------------------------------------
   ITEMS: server-side pagination, filtering, safe sorting
   - OFFSET/FETCH paging (needs ORDER BY)
   - Sorting with CASE instead of dynamic SQL: no SQL injection possible
   - "Catch-all" optional filters + OPTION (RECOMPILE): each call gets a plan
     for its actual parameters (avoids parameter sniffing on this query)
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetItemsPaged
    @Search     NVARCHAR(100) = NULL,
    @CategoryId INT           = NULL,
    @SortBy     VARCHAR(20)   = 'Name',     -- Name | Code | Price | Stock
    @SortDir    VARCHAR(4)    = 'ASC',
    @PageNumber INT           = 1,
    @PageSize   INT           = 20,
    @TotalCount INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- defensive limits: never trust the client
    SET @PageNumber = IIF(@PageNumber < 1, 1, @PageNumber);
    SET @PageSize   = IIF(@PageSize NOT BETWEEN 1 AND 100, 20, @PageSize);
    SET @Search     = NULLIF(LTRIM(RTRIM(@Search)), N'');
    -- '%' + x + '%' is NOT SARGable (no index seek). Acceptable for a small
    -- master table; for big tables prefer prefix search (x + '%') or full-text.
    DECLARE @Pattern NVARCHAR(102) = N'%' + @Search + N'%';

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Items i
    WHERE i.IsDeleted = 0
      AND (@Search IS NULL OR i.Name LIKE @Pattern OR i.Code LIKE @Pattern)
      AND (@CategoryId IS NULL OR i.CategoryId = @CategoryId)
    OPTION (RECOMPILE);

    SELECT i.Id, i.Code, i.Name, i.CategoryId, c.Name AS CategoryName, i.Unit,
           i.UnitPrice, i.TaxPercent, i.ReorderLevel, i.IsActive,
           ISNULL(s.TotalQty, 0) AS StockQty,
           CAST(CASE WHEN ISNULL(s.TotalQty, 0) <= i.ReorderLevel THEN 1 ELSE 0 END AS BIT) AS IsBelowReorder,   -- BIT maps to C# bool
           i.RowVersion
    FROM dbo.Items i
    JOIN dbo.ItemCategories c ON c.Id = i.CategoryId
    OUTER APPLY (SELECT SUM(sb.Quantity) AS TotalQty
                 FROM dbo.StockBalances sb WHERE sb.ItemId = i.Id) s
    WHERE i.IsDeleted = 0
      AND (@Search IS NULL OR i.Name LIKE @Pattern OR i.Code LIKE @Pattern)
      AND (@CategoryId IS NULL OR i.CategoryId = @CategoryId)
    ORDER BY
        -- one CASE per data type (CASE must return a single type)
        CASE WHEN @SortDir = 'ASC'  AND @SortBy = 'Name'  THEN i.Name END ASC,
        CASE WHEN @SortDir = 'DESC' AND @SortBy = 'Name'  THEN i.Name END DESC,
        CASE WHEN @SortDir = 'ASC'  AND @SortBy = 'Code'  THEN i.Code END ASC,
        CASE WHEN @SortDir = 'DESC' AND @SortBy = 'Code'  THEN i.Code END DESC,
        CASE WHEN @SortDir = 'ASC'  AND @SortBy = 'Price' THEN i.UnitPrice END ASC,
        CASE WHEN @SortDir = 'DESC' AND @SortBy = 'Price' THEN i.UnitPrice END DESC,
        CASE WHEN @SortDir = 'ASC'  AND @SortBy = 'Stock' THEN s.TotalQty END ASC,
        CASE WHEN @SortDir = 'DESC' AND @SortBy = 'Stock' THEN s.TotalQty END DESC,
        i.Id                                   -- tie-breaker: stable pages
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);
END;
GO

/* ---------------------------------------------------------------------
   DESPATCH: the core ACID transaction
   Stock balance, stock ledger, order status and the outbox event change
   together or not at all.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_DespatchOrder
    @OrderId      INT,
    @DespatchedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;            -- any run-time error dooms + rolls back the transaction

    DECLARE @Status VARCHAR(20), @WarehouseId INT, @OrderNo VARCHAR(30),
            @Now DATETIME2(3) = SYSUTCDATETIME(), @Msg NVARCHAR(400);

    BEGIN TRY
        BEGIN TRANSACTION;

        /* UPDLOCK + HOLDLOCK: read the order and keep an update lock until
           COMMIT, so a second user clicking "Despatch" at the same moment
           waits here and then sees Status = 'Despatched'. */
        SELECT @Status = Status, @WarehouseId = WarehouseId, @OrderNo = OrderNo
        FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @OrderId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50404, N'Order not found.', 1;
        IF @Status <> 'Approved'
        BEGIN
            SET @Msg = CONCAT(N'Order ', @OrderNo, N' is ', @Status, N'; only Approved orders can be despatched.');
            THROW 50409, @Msg, 1;
        END;

        /* Quantity needed per item (the same item could appear on two lines) */
        DECLARE @Need TABLE (ItemId INT PRIMARY KEY, Qty DECIMAL(18,3) NOT NULL);
        INSERT INTO @Need (ItemId, Qty)
        SELECT ItemId, SUM(Qty) FROM dbo.OrderLines WHERE OrderId = @OrderId GROUP BY ItemId;

        /* Lock the balance rows (always in ItemId order, the same order every
           caller uses, which avoids deadlocks) and find any shortfall. */
        SELECT TOP (1) @Msg = CONCAT(N'Insufficient stock for ', i.Code, N': need ', n.Qty,
                                     N', available ', ISNULL(sb.Quantity, 0), N'.')
        FROM @Need n
        JOIN dbo.Items i ON i.Id = n.ItemId
        LEFT JOIN dbo.StockBalances sb WITH (UPDLOCK, HOLDLOCK)
               ON sb.ItemId = n.ItemId AND sb.WarehouseId = @WarehouseId
        WHERE ISNULL(sb.Quantity, 0) < n.Qty
        ORDER BY n.ItemId;

        IF @Msg IS NOT NULL
            THROW 50422, @Msg, 1;

        UPDATE sb
        SET Quantity = sb.Quantity - n.Qty, ModifiedOn = @Now
        FROM dbo.StockBalances sb
        JOIN @Need n ON n.ItemId = sb.ItemId
        WHERE sb.WarehouseId = @WarehouseId;   -- CK_StockBalances_NonNegative is the last safety net

        INSERT INTO dbo.StockLedger (ItemId, WarehouseId, TxnDate, TxnType, Qty, RefDocNo, RefOrderId, CreatedBy)
        SELECT ItemId, @WarehouseId, @Now, 'DESPATCH', -Qty, @OrderNo, @OrderId, @DespatchedBy
        FROM dbo.OrderLines
        WHERE OrderId = @OrderId;

        UPDATE dbo.Orders
        SET Status = 'Despatched', ModifiedBy = @DespatchedBy, ModifiedOn = @Now
        WHERE Id = @OrderId;

        /* Transactional outbox: the event is committed with the data. If the
           broker is down, nothing is lost: the publisher retries later. */
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        SELECT 'OrderDespatched',
               (SELECT o.Id AS orderId, o.OrderNo AS orderNo, o.CustomerId AS customerId,
                       c.Name AS customerName, o.SubTotal AS subTotal, o.TaxAmount AS taxAmount,
                       o.TotalAmount AS totalAmount, @Now AS despatchedOn, @DespatchedBy AS despatchedBy,
                       (SELECT ol.ItemId AS itemId, ol.Qty AS qty, ol.LineTotal AS lineTotal
                        FROM dbo.OrderLines ol WHERE ol.OrderId = o.Id
                        FOR JSON PATH) AS lines
                FROM dbo.Orders o
                JOIN dbo.Customers c ON c.Id = o.CustomerId
                WHERE o.Id = @OrderId
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

        COMMIT TRANSACTION;

        SELECT @OrderId AS OrderId, @OrderNo AS OrderNo, 'Despatched' AS Status;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;                      -- re-throw the original error (keeps number + message)
    END CATCH
END;
GO

/* ---------------------------------------------------------------------
   SAGA COMPENSATION (used in Phase 6)
   When the Ledger service reports LedgerFailed, undo the despatch:
   put stock back with REVERSAL rows and mark the order Failed.
   Idempotent: running it twice for the same order changes nothing.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ReverseDespatch
    @OrderId    INT,
    @Reason     NVARCHAR(300),
    @ReversedBy NVARCHAR(100) = N'saga'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status VARCHAR(20), @WarehouseId INT, @OrderNo VARCHAR(30), @Now DATETIME2(3) = SYSUTCDATETIME();

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @Status = Status, @WarehouseId = WarehouseId, @OrderNo = OrderNo
        FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @OrderId;

        IF @Status IS NULL
            THROW 50404, N'Order not found.', 1;

        IF @Status = 'Despatched'          -- anything else: already compensated or never despatched
        BEGIN
            UPDATE sb
            SET Quantity = sb.Quantity + l.Qty, ModifiedOn = @Now
            FROM dbo.StockBalances sb
            JOIN (SELECT ItemId, SUM(Qty) AS Qty FROM dbo.OrderLines WHERE OrderId = @OrderId GROUP BY ItemId) l
                 ON l.ItemId = sb.ItemId
            WHERE sb.WarehouseId = @WarehouseId;

            INSERT INTO dbo.StockLedger (ItemId, WarehouseId, TxnDate, TxnType, Qty, RefDocNo, RefOrderId, Remarks, CreatedBy)
            SELECT ItemId, @WarehouseId, @Now, 'REVERSAL', Qty, @OrderNo, @OrderId, LEFT(@Reason, 200), @ReversedBy
            FROM dbo.OrderLines
            WHERE OrderId = @OrderId;

            UPDATE dbo.Orders
            SET Status = 'Failed',
                Remarks = LEFT(CONCAT(Remarks, IIF(Remarks IS NULL, N'', N' | '), N'Reversed: ', @Reason), 500),
                ModifiedBy = @ReversedBy, ModifiedOn = @Now
            WHERE Id = @OrderId;
        END;

        COMMIT TRANSACTION;
        SELECT @OrderId AS OrderId, IIF(@Status = 'Despatched', 'Failed', @Status) AS Status;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ---------------------------------------------------------------------
   REPORTS
   --------------------------------------------------------------------- */

/* Running stock per item/warehouse: the stock-ledger example from the guide.
   ROWS UNBOUNDED PRECEDING = "from the first row up to this one".
   (The default frame with ORDER BY is RANGE, which treats rows with the same
   TxnDate as one group and is slower; always write ROWS for running totals.) */
CREATE OR ALTER PROCEDURE dbo.usp_GetRunningStock
    @ItemId      INT,
    @WarehouseId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT sl.Id, w.Code AS Warehouse, sl.TxnDate, sl.TxnType, sl.RefDocNo, sl.Qty,
           SUM(sl.Qty) OVER (PARTITION BY sl.ItemId, sl.WarehouseId
                             ORDER BY sl.TxnDate, sl.Id
                             ROWS UNBOUNDED PRECEDING) AS RunningStock
    FROM dbo.StockLedger sl
    JOIN dbo.Warehouses w ON w.Id = sl.WarehouseId
    WHERE sl.ItemId = @ItemId
      AND (@WarehouseId IS NULL OR sl.WarehouseId = @WarehouseId)
    ORDER BY w.Code, sl.TxnDate, sl.Id;
END;
GO

/* Latest N orders per customer.
   CROSS APPLY = inner join to a per-row subquery (customers WITHOUT orders vanish)
   OUTER APPLY = left join to it (customers without orders stay, with NULLs). */
CREATE OR ALTER PROCEDURE dbo.usp_GetLatestOrdersPerCustomer
    @TopN            INT = 3,
    @IncludeNoOrders BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    IF @IncludeNoOrders = 1
        SELECT c.Code, c.Name, x.OrderNo, x.OrderDate, x.Status, x.TotalAmount
        FROM dbo.Customers c
        OUTER APPLY (SELECT TOP (@TopN) o.OrderNo, o.OrderDate, o.Status, o.TotalAmount
                     FROM dbo.Orders o
                     WHERE o.CustomerId = c.Id AND o.IsDeleted = 0
                     ORDER BY o.OrderDate DESC) x               -- seeks IX_Orders_Customer_Date
        WHERE c.IsDeleted = 0
        ORDER BY c.Code, x.OrderDate DESC;
    ELSE
        SELECT c.Code, c.Name, x.OrderNo, x.OrderDate, x.Status, x.TotalAmount
        FROM dbo.Customers c
        CROSS APPLY (SELECT TOP (@TopN) o.OrderNo, o.OrderDate, o.Status, o.TotalAmount
                     FROM dbo.Orders o
                     WHERE o.CustomerId = c.Id AND o.IsDeleted = 0
                     ORDER BY o.OrderDate DESC) x
        WHERE c.IsDeleted = 0
        ORDER BY c.Code, x.OrderDate DESC;
END;
GO

/* Month-wise sales with month-over-month change (LAG) and a running
   financial-year total (window SUM partitioned by financial year). */
CREATE OR ALTER PROCEDURE dbo.usp_GetMonthlySales
AS
BEGIN
    SET NOCOUNT ON;
    WITH Monthly AS
    (
        SELECT DATEFROMPARTS(YEAR(OrderDate), MONTH(OrderDate), 1) AS MonthStart,
               SUM(TotalAmount) AS Total,
               COUNT(*)         AS Orders
        FROM dbo.Orders
        WHERE Status IN ('Despatched', 'Posted') AND IsDeleted = 0
        GROUP BY DATEFROMPARTS(YEAR(OrderDate), MONTH(OrderDate), 1)   -- grouping by a date, not FORMAT(): sortable + cheaper
    )
    SELECT FORMAT(MonthStart, 'yyyy-MM')                          AS [Month],
           dbo.fn_FinancialYear(MonthStart)                       AS FinancialYear,
           Orders,
           Total,
           LAG(Total) OVER (ORDER BY MonthStart)                  AS PrevMonthTotal,
           Total - LAG(Total) OVER (ORDER BY MonthStart)          AS Change,
           CAST(100.0 * (Total - LAG(Total) OVER (ORDER BY MonthStart))
                / NULLIF(LAG(Total) OVER (ORDER BY MonthStart), 0) AS DECIMAL(9,2)) AS ChangePct,
           SUM(Total) OVER (PARTITION BY dbo.fn_FinancialYear(MonthStart)
                            ORDER BY MonthStart ROWS UNBOUNDED PRECEDING) AS FyToDate
    FROM Monthly
    ORDER BY MonthStart;
END;
GO

PRINT 'Procedures and functions created.';
GO
