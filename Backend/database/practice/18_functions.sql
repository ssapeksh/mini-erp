/* =====================================================================
   18 - Stored procedure vs function; scalar vs inline table-valued
   Procedure: can change data, call other procs, return many result sets,
              use TRY/CATCH + transactions; cannot be used inside SELECT.
   Function : returns a value or a table, usable in SELECT/WHERE/JOIN,
              cannot change data.
   ===================================================================== */
USE ErpDb;
SET NOCOUNT ON;

PRINT 'Scalar function in a SELECT: called once PER ROW';
SET STATISTICS TIME ON;
SELECT i.Code, dbo.fn_ItemStockScalar(i.Id) AS Stock
FROM dbo.Items i;

PRINT 'Inline table-valued function: expanded into the query (set-based join)';
SELECT i.Code, s.WarehouseCode, s.Quantity
FROM dbo.Items i
CROSS APPLY dbo.fn_ItemStock(i.Id) s
ORDER BY i.Code, s.WarehouseCode;
SET STATISTICS TIME OFF;
/* Interview line: "Scalar UDFs that read tables behave like a hidden cursor.
   SQL Server 2019+ can inline simple ones automatically (check
   sys.sql_modules.is_inlineable), but an inline TVF is always set-based." */
SELECT OBJECT_NAME(object_id) AS FunctionName, is_inlineable
FROM sys.sql_modules
WHERE OBJECT_NAME(object_id) LIKE 'fn[_]%';

PRINT 'Scalar function that does not read tables: cheap and inlineable';
SELECT dbo.fn_FinancialYear('2027-03-31') AS FyMarch, dbo.fn_FinancialYear('2027-04-01') AS FyApril;

PRINT 'Procedure with an OUTPUT parameter (document number from a SEQUENCE)';
DECLARE @No VARCHAR(30);
EXEC dbo.usp_GetNextOrderNo @OrderDate = '2026-11-26', @OrderNo = @No OUTPUT;
SELECT @No AS NextOrderNo;
GO
