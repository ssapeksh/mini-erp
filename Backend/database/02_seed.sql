/* =====================================================================
   02_seed.sql  -  demo data for ErpDb (deterministic, re-creatable)

   Demo logins (password for all: Demo@123)
     admin      Admin        - everything
     manager1   Manager      - level-2 approver
     approver1  Approver     - level-1 approver
     store1     StoreKeeper  - creates and despatches orders
     viewer1    Viewer       - read only
   Hashes are ASP.NET Core Identity PasswordHasher v3 (PBKDF2-HMACSHA512).

   Interview topics: DML (INSERT ... SELECT), set-based inserts with a
   numbers CTE instead of loops, NEXT VALUE FOR a SEQUENCE, OVER (ORDER BY)
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any error rolls back the whole transaction (without it, SQL Server may continue and COMMIT half the work)
USE ErpDb;
GO

BEGIN TRANSACTION;

/* ---------- security ---------- */
INSERT INTO dbo.Roles (Name, Description) VALUES
 (N'Admin',       N'Full access'),
 (N'Manager',     N'Manages masters, level-2 approval'),
 (N'Approver',    N'Level-1 approval'),
 (N'StoreKeeper', N'Creates and despatches orders, adjusts stock'),
 (N'Viewer',      N'Read only');

INSERT INTO dbo.Permissions (Code, Module, Description) VALUES
 ('Items.View',         'Items',   N'View items'),
 ('Items.Create',       'Items',   N'Create items'),
 ('Items.Edit',         'Items',   N'Edit items'),
 ('Items.Delete',       'Items',   N'Soft-delete items'),
 ('Orders.View',        'Orders',  N'View orders'),
 ('Orders.Create',      'Orders',  N'Create and submit orders'),
 ('Orders.Approve.L1',  'Orders',  N'Level-1 approval'),
 ('Orders.Approve.L2',  'Orders',  N'Level-2 approval'),
 ('Orders.Despatch',    'Orders',  N'Despatch approved orders'),
 ('Stock.View',         'Stock',   N'View stock and ledger'),
 ('Stock.Adjust',       'Stock',   N'Post stock adjustments'),
 ('Reports.View',       'Reports', N'View reports'),
 ('Ledger.View',        'Ledger',  N'View accounting ledger'),
 ('Users.Manage',       'Users',   N'Manage users and sessions');

/* Role -> permission matrix, written as data so it reads like a table */
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM (VALUES
    (N'Admin', '*'),
    (N'Manager', 'Items.View'), (N'Manager', 'Items.Create'), (N'Manager', 'Items.Edit'), (N'Manager', 'Items.Delete'),
    (N'Manager', 'Orders.View'), (N'Manager', 'Orders.Create'), (N'Manager', 'Orders.Approve.L2'),
    (N'Manager', 'Stock.View'), (N'Manager', 'Reports.View'), (N'Manager', 'Ledger.View'),
    (N'Approver', 'Items.View'), (N'Approver', 'Orders.View'), (N'Approver', 'Orders.Approve.L1'),
    (N'Approver', 'Stock.View'), (N'Approver', 'Reports.View'),
    (N'StoreKeeper', 'Items.View'), (N'StoreKeeper', 'Items.Create'), (N'StoreKeeper', 'Items.Edit'),
    (N'StoreKeeper', 'Orders.View'), (N'StoreKeeper', 'Orders.Create'), (N'StoreKeeper', 'Orders.Despatch'),
    (N'StoreKeeper', 'Stock.View'), (N'StoreKeeper', 'Stock.Adjust'),
    (N'Viewer', 'Items.View'), (N'Viewer', 'Orders.View'), (N'Viewer', 'Stock.View')
) AS m(RoleName, PermCode)
JOIN dbo.Roles r       ON r.Name = m.RoleName
JOIN dbo.Permissions p ON p.Code = m.PermCode OR m.PermCode = '*';

/* Users: ManagerId builds a hierarchy (used by the recursive CTE demo) */
INSERT INTO dbo.Users (UserName, Email, FullName, PasswordHash, RoleId) VALUES
 (N'admin',     N'admin@minierp.local',     N'Admin User',
  N'AQAAAAIAAYagAAAAEGPgUfBRsazdMJq11BdhTRuS82uC3XCqEdPNbMQbTMOGuQR8AM+xJEUs3nN9T2pqqg==', (SELECT Id FROM dbo.Roles WHERE Name = N'Admin')),
 (N'manager1',  N'manager1@minierp.local',  N'Mohan Kulkarni',
  N'AQAAAAIAAYagAAAAELs8KgPJFlZH7jZ5EKM1s0aT0EDPEM3IKy6f6bzoCHQ8vO0PQ19IDVDGdRYCvhw59g==', (SELECT Id FROM dbo.Roles WHERE Name = N'Manager')),
 (N'approver1', N'approver1@minierp.local', N'Anita Deshmukh',
  N'AQAAAAIAAYagAAAAEJ1gU7qxGoMZStu7V9s1CH6diV+rzAHB0qd5nkPIZHg39u15Yn8XiFZKGwvLB9Z4gQ==', (SELECT Id FROM dbo.Roles WHERE Name = N'Approver')),
 (N'store1',    N'store1@minierp.local',    N'Suresh Patil',
  N'AQAAAAIAAYagAAAAEEVt99YuopNf1M+bdHmFIfjDBkV7+mKDpnCTHJDRHvaU0M4cb0s93MDga9cFShecsA==', (SELECT Id FROM dbo.Roles WHERE Name = N'StoreKeeper')),
 (N'viewer1',   N'viewer1@minierp.local',   N'Vikram Joshi',
  N'AQAAAAIAAYagAAAAENihDSEO72cVTgzgfqpzMVHMEZHUAPqroGdLABTMdU0xKyIZFUlZ91Y/+Kxu5flhug==', (SELECT Id FROM dbo.Roles WHERE Name = N'Viewer'));

UPDATE u SET ManagerId = m.Id
FROM dbo.Users u
JOIN (VALUES (N'manager1', N'admin'), (N'approver1', N'manager1'),
             (N'store1', N'manager1'), (N'viewer1', N'approver1')) AS x(UserName, ManagerName)
     ON x.UserName = u.UserName
JOIN dbo.Users m ON m.UserName = x.ManagerName;

/* ---------- masters ---------- */
INSERT INTO dbo.Warehouses (Code, Name, City) VALUES
 ('MUM', N'Mumbai Central Warehouse', N'Mumbai'),
 ('PUN', N'Pune Warehouse',           N'Pune');

INSERT INTO dbo.ItemCategories (Name) VALUES (N'Electrical'), (N'Plumbing'), (N'Hardware'), (N'Paints');

INSERT INTO dbo.Items (Code, Name, CategoryId, Unit, UnitPrice, TaxPercent, ReorderLevel)
SELECT v.Code, v.Name, c.Id, v.Unit, v.Price, v.Tax, v.Reorder
FROM (VALUES
 ('ELE-001', N'LED Bulb 9W',                              N'Electrical', 'NOS',  95.00, 18, 100),
 ('ELE-002', N'LED Tube Light 20W',                       N'Electrical', 'NOS', 320.00, 18,  50),
 ('ELE-003', N'Copper Wire 1.5 sq mm (तांबे की तार)',      N'Electrical', 'MTR',  28.50, 18, 500),  -- Unicode: needs NVARCHAR
 ('ELE-004', N'Modular Switch 6A',                        N'Electrical', 'NOS',  65.00, 18, 200),
 ('ELE-005', N'MCB 32A Double Pole',                      N'Electrical', 'NOS', 540.00, 18,  30),
 ('PLM-001', N'PVC Pipe 1 inch (3 m)',                    N'Plumbing',   'NOS', 210.00, 18,  80),
 ('PLM-002', N'Brass Ball Valve 1/2 inch',                N'Plumbing',   'NOS', 385.00, 18,  40),
 ('PLM-003', N'Teflon Tape',                              N'Plumbing',   'NOS',  18.00, 12, 300),
 ('PLM-004', N'Water Tank 500 L',                         N'Plumbing',   'NOS',4250.00, 18,   5),
 ('HDW-001', N'Wood Screw 1 inch (box of 100)',           N'Hardware',   'BOX', 145.00, 18,  60),
 ('HDW-002', N'Door Hinge 4 inch',                        N'Hardware',   'PAIR', 90.00, 18, 100),
 ('HDW-003', N'Padlock 50 mm',                            N'Hardware',   'NOS', 260.00, 18,  40),
 ('HDW-004', N'Wall Plug 8 mm (pack of 50)',              N'Hardware',   'PKT',  55.00, 18, 120),
 ('PNT-001', N'Interior Emulsion White 10 L',             N'Paints',     'NOS',2350.00, 28,  20),
 ('PNT-002', N'Wall Primer 4 L',                          N'Paints',     'NOS', 690.00, 28,  25),
 ('PNT-003', N'Paint Brush 2 inch',                       N'Paints',     'NOS',  75.00, 18, 150)
) AS v(Code, Name, Category, Unit, Price, Tax, Reorder)
JOIN dbo.ItemCategories c ON c.Name = v.Category;

INSERT INTO dbo.Customers (Code, Name, Email, City, GSTIN, CreditLimit) VALUES
 ('C001', N'Shree Ganesh Traders',     N'accounts@ganeshtraders.example',  N'Mumbai', '27AABCS1234A1Z5', 500000),
 ('C002', N'Om Sai Electricals',       N'om.sai@example.com',              N'Thane',  '27AAACO5678B1Z2', 300000),
 ('C003', N'Patel Hardware Mart',      N'patelhw@example.com',             N'Pune',   NULL,              150000),
 ('C004', N'Konkan Builders',          N'purchase@konkanbuilders.example', N'Ratnagiri','27AAFCK9012C1Z8', 800000),
 ('C005', N'City Plumbing Solutions',  NULL,                               N'Nashik', NULL,              100000),
 ('C006', N'Deccan Paints & Decor',    N'deccan@example.com',              N'Pune',   '27AADCD3456D1Z1', 250000),
 ('C007', N'Sahyadri Infra Projects',  N'infra@sahyadri.example',          N'Satara', '27AAHCS7890E1Z4', 900000),
 ('C008', N'New Horizon Interiors',    N'hello@newhorizon.example',        N'Mumbai', NULL,              200000);  -- has NO orders (anti-join demo)

/* ---------- stock: opening balance + monthly receipts ---------- */
INSERT INTO dbo.StockLedger (ItemId, WarehouseId, TxnDate, TxnType, Qty, RefDocNo, Remarks)
SELECT i.Id, w.Id, '2025-11-01T09:00:00', 'OPENING',
       400 + (i.Id * 37 + w.Id * 11) % 400,          -- deterministic 400..799
       'OB/2025-26', N'Opening balance'
FROM dbo.Items i CROSS JOIN dbo.Warehouses w;          -- CROSS JOIN: every item in every warehouse

;WITH Months AS (
    SELECT 1 AS m UNION ALL SELECT m + 1 FROM Months WHERE m < 12          -- small recursive CTE as a numbers table
)
INSERT INTO dbo.StockLedger (ItemId, WarehouseId, TxnDate, TxnType, Qty, RefDocNo, Remarks)
SELECT i.Id, w.Id,
       DATEADD(DAY, 4, DATEADD(MONTH, mo.m, CAST('2025-11-01T10:30:00' AS DATETIME2(3)))),
       'RECEIPT',
       20 + (i.Id * 13 + w.Id * 7 + mo.m * 5) % 60,
       CONCAT('GRN/', FORMAT(mo.m, '000'), '/', w.Code), N'Supplier receipt'
FROM dbo.Items i
CROSS JOIN dbo.Warehouses w
CROSS JOIN Months mo
WHERE (i.Id + mo.m) % 3 = 0                            -- not every item every month
  AND DATEADD(MONTH, mo.m, CAST('2025-11-01' AS DATE)) <= '2026-11-20';

/* ---------- 120 sales orders over the last 12 months ---------- */
;WITH N AS (
    SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects                                -- quick numbers source
), Src AS (
    SELECT n,
           DaysAgo     = (n * 37) % 360,
           CustomerId  = (n * 5) % 7 + 1,               -- customers 1..7 (C008 never orders)
           WarehouseId = n % 2 + 1
    FROM N
    WHERE n <= 120                                      -- (NEXT VALUE FOR is not allowed with TOP)
), Dated AS (
    SELECT *, OrderDate = DATEADD(DAY, -DaysAgo, CAST('2026-11-25' AS DATE)),
           Status = CASE
                      WHEN DaysAgo < 40 THEN CHOOSE(n % 5 + 1, 'Draft', 'PendingL1', 'PendingL2', 'Approved', 'Despatched')
                      WHEN n % 17 = 0   THEN 'Rejected'
                      ELSE 'Posted'
                    END
    FROM Src
)
INSERT INTO dbo.Orders (OrderNo, CustomerId, WarehouseId, OrderDate, Status, Remarks, CreatedBy, CreatedOn)
SELECT CONCAT('SO/',
              CASE WHEN MONTH(OrderDate) >= 4                       -- Indian financial year: April to March
                   THEN CONCAT(YEAR(OrderDate), '-', RIGHT(YEAR(OrderDate) + 1, 2))
                   ELSE CONCAT(YEAR(OrderDate) - 1, '-', RIGHT(YEAR(OrderDate), 2)) END,
              '/', FORMAT(NEXT VALUE FOR dbo.OrderNoSeq OVER (ORDER BY OrderDate, n), '000000')),
       CustomerId, WarehouseId, OrderDate, Status, NULL, N'store1',
       DATEADD(MINUTE, 600 + (n * 13) % 420, CAST(OrderDate AS DATETIME2(3)))
FROM Dated;

/* 1-4 lines per order; (Id*7 + LineNumber*3) % 16 never repeats an item within an order */
;WITH L AS (SELECT 1 AS LineNumber UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4)
INSERT INTO dbo.OrderLines (OrderId, LineNumber, ItemId, Qty, UnitPrice, DiscountPct, TaxPercent)
SELECT o.Id, l.LineNumber, i.Id, q.Qty, i.UnitPrice,
       CASE WHEN q.Qty >= 15 THEN 5 ELSE 0 END,        -- bulk discount
       i.TaxPercent
FROM dbo.Orders o
JOIN L ON l.LineNumber <= o.Id % 4 + 1
CROSS APPLY (SELECT ItemId = (o.Id * 7 + l.LineNumber * 3) % 16 + 1,
                    Qty    = CAST((o.Id + l.LineNumber * 5) % 20 + 1 AS DECIMAL(18,3))) AS q
JOIN dbo.Items i ON i.Id = q.ItemId;

/* Order totals from their lines (correlated aggregate via APPLY) */
UPDATE o
SET SubTotal  = t.SubTotal,
    TaxAmount = t.TaxAmount
FROM dbo.Orders o
CROSS APPLY (SELECT SUM(ol.LineTotal) AS SubTotal,
                    CAST(SUM(ol.LineTotal * ol.TaxPercent / 100) AS DECIMAL(18,2)) AS TaxAmount
             FROM dbo.OrderLines ol WHERE ol.OrderId = o.Id) AS t;

/* Approval trail matching each order's status */
DECLARE @store INT    = (SELECT Id FROM dbo.Users WHERE UserName = N'store1'),
        @approver INT = (SELECT Id FROM dbo.Users WHERE UserName = N'approver1'),
        @manager INT  = (SELECT Id FROM dbo.Users WHERE UserName = N'manager1');

INSERT INTO dbo.ApprovalHistory (OrderId, Level, Action, ActionBy, ActionOn, Comments)
SELECT o.Id, s.Level, s.Action, s.ActionBy, DATEADD(HOUR, s.HoursAfter, o.CreatedOn), s.Comments
FROM dbo.Orders o
CROSS APPLY (VALUES
    (0, 'Submitted', @store,    1, NULL,                     1),
    (1, 'Approved',  @approver, 5, N'Rates verified',        2),
    (1, 'Rejected',  @approver, 5, N'Credit limit exceeded', 3),
    (2, 'Approved',  @manager, 20, N'OK',                    4)
) AS s(Level, Action, ActionBy, HoursAfter, Comments, Step)
WHERE (s.Step = 1 AND o.Status <> 'Draft')
   OR (s.Step = 2 AND o.Status IN ('PendingL2','Approved','Despatched','Posted'))
   OR (s.Step = 3 AND o.Status = 'Rejected')
   OR (s.Step = 4 AND o.Status IN ('Approved','Despatched','Posted'));

/* Despatched / posted orders moved stock out (negative quantities) */
INSERT INTO dbo.StockLedger (ItemId, WarehouseId, TxnDate, TxnType, Qty, RefDocNo, RefOrderId, CreatedBy)
SELECT ol.ItemId, o.WarehouseId, DATEADD(DAY, 1, o.CreatedOn), 'DESPATCH', -ol.Qty, o.OrderNo, o.Id, N'store1'
FROM dbo.Orders o
JOIN dbo.OrderLines ol ON ol.OrderId = o.Id
WHERE o.Status IN ('Despatched', 'Posted');

/* Current balance = SUM of the ledger (the ledger is the source of truth) */
INSERT INTO dbo.StockBalances (ItemId, WarehouseId, Quantity)
SELECT ItemId, WarehouseId, SUM(Qty)
FROM dbo.StockLedger
GROUP BY ItemId, WarehouseId;

/* Reporting copy for the columnstore demo */
INSERT INTO dbo.SalesFact (OrderId, OrderDate, CustomerId, ItemId, CategoryId, WarehouseId, Qty, NetAmount)
SELECT o.Id, o.OrderDate, o.CustomerId, ol.ItemId, i.CategoryId, o.WarehouseId, ol.Qty, ol.LineTotal
FROM dbo.Orders o
JOIN dbo.OrderLines ol ON ol.OrderId = o.Id
JOIN dbo.Items i       ON i.Id = ol.ItemId
WHERE o.Status = 'Posted';

/* Supplier price file waiting to be MERGEd (two price changes, one new item,
   one unchanged row) - see usp_SyncItemPrices in 04_views_triggers_merge.sql */
INSERT INTO dbo.ItemPriceImport (Code, Name, Category, UnitPrice) VALUES
 ('ELE-001', N'LED Bulb 9W',              N'Electrical',  99.00),   -- price up
 ('PLM-003', N'Teflon Tape',              N'Plumbing',    16.50),   -- price down
 ('HDW-002', N'Door Hinge 4 inch',        N'Hardware',    90.00),   -- unchanged: MERGE must skip it
 ('ELE-006', N'Ceiling Fan 1200 mm',      N'Electrical', 1850.00);  -- new item

COMMIT TRANSACTION;
GO

DECLARE @orders INT = (SELECT COUNT(*) FROM dbo.Orders),
        @lines  INT = (SELECT COUNT(*) FROM dbo.OrderLines),
        @ledger INT = (SELECT COUNT(*) FROM dbo.StockLedger);
PRINT CONCAT('Seeded: ', @orders, ' orders, ', @lines, ' lines, ', @ledger, ' ledger rows.');   -- PRINT cannot contain subqueries
GO
