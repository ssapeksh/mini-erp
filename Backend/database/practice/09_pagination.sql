/* =====================================================================
   09 - Server-side pagination
   OFFSET/FETCH (page numbers) vs keyset / seek pagination (big tables)
   The API uses ErpDb.dbo.usp_GetItemsPaged.
   ===================================================================== */
USE PracticeDb;
SET NOCOUNT ON;

DECLARE @Page INT = 3, @PageSize INT = 10;

PRINT 'OFFSET/FETCH: page 3 of 10 rows, plus the total in the same query';
SELECT Id, SaleDate, Amount,
       COUNT(*) OVER () AS TotalRows          -- total without a second query (computed before OFFSET)
FROM dbo.Sales
ORDER BY SaleDate, Id                          -- ORDER BY is mandatory and must be unique (Id breaks ties)
OFFSET (@Page - 1) * @PageSize ROWS
FETCH NEXT @PageSize ROWS ONLY;
/* Cost: page 1000 still reads and throws away 9,990 rows. */

PRINT 'Keyset (seek) pagination: "give me 10 rows after the last one I saw"';
DECLARE @LastDate DATE = '2025-06-01', @LastId INT = 0;
SELECT TOP (@PageSize) Id, SaleDate, Amount
FROM dbo.Sales
WHERE SaleDate > @LastDate
   OR (SaleDate = @LastDate AND Id > @LastId)  -- tie-breaker on the unique key
ORDER BY SaleDate, Id;
/* Cost is the same for every page (index seek), but you can't jump to page N;
   good for infinite scroll and exports. */

PRINT 'Real project: ErpDb items page 1, sorted by price desc';
DECLARE @Total INT;
EXEC ErpDb.dbo.usp_GetItemsPaged @SortBy = 'Price', @SortDir = 'DESC', @PageNumber = 1, @PageSize = 5,
                                 @TotalCount = @Total OUTPUT;
SELECT @Total AS TotalItems;
GO
