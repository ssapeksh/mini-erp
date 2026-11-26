/* =====================================================================
   01_schema.sql  -  ErpDb tables, keys, constraints and indexes

   Interview topics shown here
   - DDL (CREATE / ALTER), constraints: PRIMARY KEY, FOREIGN KEY, UNIQUE,
     CHECK, DEFAULT, NOT NULL
   - ON DELETE CASCADE (OrderLines, RolePermissions, ...)
   - Audit / soft-delete columns on every business table (BaseEntity in C#)
   - rowversion for optimistic concurrency (EF Core concurrency token)
   - NVARCHAR for Unicode text (Hindi/Marathi names), VARCHAR for codes
   - SEQUENCE for document numbers (no MAX(Id)+1 race conditions)
   - Temporal (system-versioned) table: Items keeps full history
   - Computed, persisted column (OrderLines.LineTotal)
   - Indexes: clustered PK, non-clustered, covering (INCLUDE),
     filtered (WHERE IsDeleted = 0), unique filtered,
     non-clustered columnstore for reporting
   - Normalisation: see the note at the bottom of this file
   ===================================================================== */
SET NOCOUNT ON;
USE ErpDb;
GO

/* ---------------------------------------------------------------------
   Security: roles, permissions, users, sessions, refresh tokens
   Permission-based RBAC: User -> Role -> RolePermissions -> Permission
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Roles
(
    Id          INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED,
    Name        NVARCHAR(50)       NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE,
    Description NVARCHAR(200)      NULL,
    -- audit + soft delete (same columns as BaseEntity in Erp.Core)
    CreatedBy   NVARCHAR(100)      NOT NULL CONSTRAINT DF_Roles_CreatedBy DEFAULT (N'system'),
    CreatedOn   DATETIME2(3)       NOT NULL CONSTRAINT DF_Roles_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy  NVARCHAR(100)      NULL,
    ModifiedOn  DATETIME2(3)       NULL,
    IsDeleted   BIT                NOT NULL CONSTRAINT DF_Roles_IsDeleted DEFAULT (0)
);

CREATE TABLE dbo.Permissions
(
    Id          INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY CLUSTERED,
    Code        VARCHAR(100)       NOT NULL CONSTRAINT UQ_Permissions_Code UNIQUE,   -- e.g. 'Orders.Approve.L1'
    Module      VARCHAR(50)        NOT NULL,
    Description NVARCHAR(200)      NULL
);

CREATE TABLE dbo.RolePermissions
(
    RoleId       INT NOT NULL CONSTRAINT FK_RolePermissions_Roles       REFERENCES dbo.Roles(Id)       ON DELETE CASCADE,
    PermissionId INT NOT NULL CONSTRAINT FK_RolePermissions_Permissions REFERENCES dbo.Permissions(Id) ON DELETE CASCADE,
    CONSTRAINT PK_RolePermissions PRIMARY KEY CLUSTERED (RoleId, PermissionId)   -- composite key
);

CREATE TABLE dbo.Users
(
    Id           INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Users PRIMARY KEY CLUSTERED,
    UserName     NVARCHAR(50)       NOT NULL CONSTRAINT UQ_Users_UserName UNIQUE,
    Email        NVARCHAR(256)      NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
    FullName     NVARCHAR(100)      NOT NULL,
    PasswordHash NVARCHAR(500)      NOT NULL,
    RoleId       INT                NOT NULL CONSTRAINT FK_Users_Roles REFERENCES dbo.Roles(Id),
    ManagerId    INT                NULL     CONSTRAINT FK_Users_Manager REFERENCES dbo.Users(Id),  -- self reference (hierarchy)
    IsActive     BIT                NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    LastLoginOn  DATETIME2(3)       NULL,
    CreatedBy    NVARCHAR(100)      NOT NULL CONSTRAINT DF_Users_CreatedBy DEFAULT (N'system'),
    CreatedOn    DATETIME2(3)       NOT NULL CONSTRAINT DF_Users_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy   NVARCHAR(100)      NULL,
    ModifiedOn   DATETIME2(3)       NULL,
    IsDeleted    BIT                NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT (0),
    RowVersion   ROWVERSION         NOT NULL,
    CONSTRAINT CK_Users_NotOwnManager CHECK (ManagerId IS NULL OR ManagerId <> Id)
);
CREATE NONCLUSTERED INDEX IX_Users_ManagerId ON dbo.Users (ManagerId);   -- index every FK you join on
CREATE NONCLUSTERED INDEX IX_Users_RoleId    ON dbo.Users (RoleId);

/* One row per login. The filtered UNIQUE index below allows only ONE active
   session per user: the database itself enforces single-session login. */
CREATE TABLE dbo.UserSessions
(
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_UserSessions PRIMARY KEY CLUSTERED
                                             CONSTRAINT DF_UserSessions_Id DEFAULT (NEWSEQUENTIALID()), -- sequential GUID avoids page splits
    UserId         INT              NOT NULL CONSTRAINT FK_UserSessions_Users REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    IpAddress      VARCHAR(45)      NULL,          -- 45 chars fits IPv6
    UserAgent      NVARCHAR(300)    NULL,
    StartedOn      DATETIME2(3)     NOT NULL CONSTRAINT DF_UserSessions_StartedOn DEFAULT (SYSUTCDATETIME()),
    LastActivityOn DATETIME2(3)     NOT NULL CONSTRAINT DF_UserSessions_LastActivityOn DEFAULT (SYSUTCDATETIME()),
    EndedOn        DATETIME2(3)     NULL,
    EndReason      VARCHAR(30)      NULL,
    CONSTRAINT CK_UserSessions_EndReason CHECK (EndReason IS NULL OR EndReason IN ('Logout','IdleTimeout','NewLogin','Revoked'))
);
CREATE UNIQUE NONCLUSTERED INDEX UX_UserSessions_OneActivePerUser
    ON dbo.UserSessions (UserId)
    WHERE EndedOn IS NULL;                                   -- unique + filtered index
CREATE NONCLUSTERED INDEX IX_UserSessions_Active_LastActivity
    ON dbo.UserSessions (LastActivityOn)
    INCLUDE (UserId)
    WHERE EndedOn IS NULL;                                   -- used by the idle-logout background job

CREATE TABLE dbo.RefreshTokens
(
    Id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY CLUSTERED,
    UserId              INT           NOT NULL CONSTRAINT FK_RefreshTokens_Users REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    SessionId           UNIQUEIDENTIFIER NULL CONSTRAINT FK_RefreshTokens_Sessions REFERENCES dbo.UserSessions(Id),
    TokenHash           VARCHAR(128)  NOT NULL CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE,  -- store a hash, never the raw token
    ExpiresOn           DATETIME2(3)  NOT NULL,
    CreatedOn           DATETIME2(3)  NOT NULL CONSTRAINT DF_RefreshTokens_CreatedOn DEFAULT (SYSUTCDATETIME()),
    RevokedOn           DATETIME2(3)  NULL,
    ReplacedByTokenHash VARCHAR(128)  NULL                 -- rotation chain
);
CREATE NONCLUSTERED INDEX IX_RefreshTokens_UserId ON dbo.RefreshTokens (UserId) WHERE RevokedOn IS NULL;

/* ---------------------------------------------------------------------
   Master data: customers, warehouses, categories, items
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Customers
(
    Id         INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED,
    Code       VARCHAR(20)        NOT NULL CONSTRAINT UQ_Customers_Code UNIQUE,
    Name       NVARCHAR(150)      NOT NULL,
    Email      NVARCHAR(256)      NULL,
    City       NVARCHAR(80)       NULL,
    GSTIN      CHAR(15)           NULL,   -- fixed length: GST numbers are always 15 chars (CHAR vs VARCHAR)
    CreditLimit DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Customers_CreditLimit DEFAULT (0)
                                           CONSTRAINT CK_Customers_CreditLimit CHECK (CreditLimit >= 0),
    CreatedBy  NVARCHAR(100)      NOT NULL CONSTRAINT DF_Customers_CreatedBy DEFAULT (N'system'),
    CreatedOn  DATETIME2(3)       NOT NULL CONSTRAINT DF_Customers_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy NVARCHAR(100)      NULL,
    ModifiedOn DATETIME2(3)       NULL,
    IsDeleted  BIT                NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT (0),
    RowVersion ROWVERSION         NOT NULL
);

CREATE TABLE dbo.Warehouses
(
    Id         INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY CLUSTERED,
    Code       VARCHAR(10)        NOT NULL CONSTRAINT UQ_Warehouses_Code UNIQUE,
    Name       NVARCHAR(100)      NOT NULL,
    City       NVARCHAR(80)       NOT NULL,
    CreatedBy  NVARCHAR(100)      NOT NULL CONSTRAINT DF_Warehouses_CreatedBy DEFAULT (N'system'),
    CreatedOn  DATETIME2(3)       NOT NULL CONSTRAINT DF_Warehouses_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy NVARCHAR(100)      NULL,
    ModifiedOn DATETIME2(3)       NULL,
    IsDeleted  BIT                NOT NULL CONSTRAINT DF_Warehouses_IsDeleted DEFAULT (0)
);

CREATE TABLE dbo.ItemCategories
(
    Id         INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_ItemCategories PRIMARY KEY CLUSTERED,
    Name       NVARCHAR(100)      NOT NULL CONSTRAINT UQ_ItemCategories_Name UNIQUE,
    CreatedBy  NVARCHAR(100)      NOT NULL CONSTRAINT DF_ItemCategories_CreatedBy DEFAULT (N'system'),
    CreatedOn  DATETIME2(3)       NOT NULL CONSTRAINT DF_ItemCategories_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy NVARCHAR(100)      NULL,
    ModifiedOn DATETIME2(3)       NULL,
    IsDeleted  BIT                NOT NULL CONSTRAINT DF_ItemCategories_IsDeleted DEFAULT (0)
);

/* Items is a TEMPORAL table: SQL Server copies the old version of a row into
   dbo.ItemsHistory on every UPDATE/DELETE. Query history with
   FOR SYSTEM_TIME AS OF / BETWEEN (see 04_views_triggers_merge.sql). */
CREATE TABLE dbo.Items
(
    Id           INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Items PRIMARY KEY CLUSTERED,
    Code         VARCHAR(30)        NOT NULL CONSTRAINT UQ_Items_Code UNIQUE,
    Name         NVARCHAR(150)      NOT NULL,
    CategoryId   INT                NOT NULL CONSTRAINT FK_Items_Categories REFERENCES dbo.ItemCategories(Id),
    Unit         VARCHAR(10)        NOT NULL CONSTRAINT DF_Items_Unit DEFAULT ('NOS'),
    UnitPrice    DECIMAL(18,2)      NOT NULL CONSTRAINT CK_Items_UnitPrice CHECK (UnitPrice >= 0),
    TaxPercent   DECIMAL(5,2)       NOT NULL CONSTRAINT DF_Items_TaxPercent DEFAULT (18)
                                             CONSTRAINT CK_Items_TaxPercent CHECK (TaxPercent BETWEEN 0 AND 28),
    ReorderLevel DECIMAL(18,3)      NOT NULL CONSTRAINT DF_Items_ReorderLevel DEFAULT (0)
                                             CONSTRAINT CK_Items_ReorderLevel CHECK (ReorderLevel >= 0),
    IsActive     BIT                NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT (1),
    CreatedBy    NVARCHAR(100)      NOT NULL CONSTRAINT DF_Items_CreatedBy DEFAULT (N'system'),
    CreatedOn    DATETIME2(3)       NOT NULL CONSTRAINT DF_Items_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy   NVARCHAR(100)      NULL,
    ModifiedOn   DATETIME2(3)       NULL,
    IsDeleted    BIT                NOT NULL CONSTRAINT DF_Items_IsDeleted DEFAULT (0),
    RowVersion   ROWVERSION         NOT NULL,
    ValidFrom    DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidTo      DATETIME2(3) GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.ItemsHistory));

CREATE NONCLUSTERED INDEX IX_Items_CategoryId ON dbo.Items (CategoryId);
/* Filtered + covering: the item list screen only shows non-deleted rows and
   needs Code/Name/Price, so the query never touches the clustered index. */
CREATE NONCLUSTERED INDEX IX_Items_Name_Active
    ON dbo.Items (Name)
    INCLUDE (Code, UnitPrice, CategoryId, Unit, IsActive)
    WHERE IsDeleted = 0;

/* ---------------------------------------------------------------------
   Inventory: current balance + append-only stock ledger
   --------------------------------------------------------------------- */
CREATE TABLE dbo.StockBalances
(
    ItemId      INT            NOT NULL CONSTRAINT FK_StockBalances_Items      REFERENCES dbo.Items(Id),
    WarehouseId INT            NOT NULL CONSTRAINT FK_StockBalances_Warehouses REFERENCES dbo.Warehouses(Id),
    Quantity    DECIMAL(18,3)  NOT NULL CONSTRAINT DF_StockBalances_Quantity DEFAULT (0)
                                        CONSTRAINT CK_StockBalances_NonNegative CHECK (Quantity >= 0), -- stock can never go negative
    ModifiedOn  DATETIME2(3)   NOT NULL CONSTRAINT DF_StockBalances_ModifiedOn DEFAULT (SYSUTCDATETIME()),
    RowVersion  ROWVERSION     NOT NULL,
    CONSTRAINT PK_StockBalances PRIMARY KEY CLUSTERED (ItemId, WarehouseId)
);

CREATE TABLE dbo.StockLedger
(
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockLedger PRIMARY KEY CLUSTERED,
    ItemId      INT            NOT NULL CONSTRAINT FK_StockLedger_Items      REFERENCES dbo.Items(Id),
    WarehouseId INT            NOT NULL CONSTRAINT FK_StockLedger_Warehouses REFERENCES dbo.Warehouses(Id),
    TxnDate     DATETIME2(3)   NOT NULL,
    TxnType     VARCHAR(20)    NOT NULL CONSTRAINT CK_StockLedger_TxnType
                                        CHECK (TxnType IN ('OPENING','RECEIPT','DESPATCH','ADJUSTMENT','REVERSAL')),
    Qty         DECIMAL(18,3)  NOT NULL CONSTRAINT CK_StockLedger_QtyNotZero CHECK (Qty <> 0),  -- + in, - out
    RefDocNo    VARCHAR(30)    NULL,
    RefOrderId  INT            NULL,
    Remarks     NVARCHAR(200)  NULL,
    CreatedBy   NVARCHAR(100)  NOT NULL CONSTRAINT DF_StockLedger_CreatedBy DEFAULT (N'system'),
    CreatedOn   DATETIME2(3)   NOT NULL CONSTRAINT DF_StockLedger_CreatedOn DEFAULT (SYSUTCDATETIME())
);
/* Supports the running-stock window function: PARTITION BY ItemId, WarehouseId ORDER BY TxnDate */
CREATE NONCLUSTERED INDEX IX_StockLedger_Item_Wh_Date
    ON dbo.StockLedger (ItemId, WarehouseId, TxnDate)
    INCLUDE (Qty, TxnType);
CREATE NONCLUSTERED INDEX IX_StockLedger_RefOrderId ON dbo.StockLedger (RefOrderId) WHERE RefOrderId IS NOT NULL;

/* ---------------------------------------------------------------------
   Sales orders with multi-level approval
   Status flow: Draft -> PendingL1 -> PendingL2 -> Approved -> Despatched -> Posted
                                 \-> Rejected                       \-> Failed (saga compensation)
   --------------------------------------------------------------------- */
CREATE SEQUENCE dbo.OrderNoSeq AS INT START WITH 1 INCREMENT BY 1 CACHE 20;

CREATE TABLE dbo.Orders
(
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED,
    OrderNo        VARCHAR(30)       NOT NULL CONSTRAINT UQ_Orders_OrderNo UNIQUE,  -- e.g. SO/2026-27/000042
    CustomerId     INT               NOT NULL CONSTRAINT FK_Orders_Customers  REFERENCES dbo.Customers(Id),
    WarehouseId    INT               NOT NULL CONSTRAINT FK_Orders_Warehouses REFERENCES dbo.Warehouses(Id),
    OrderDate      DATE              NOT NULL,
    Status         VARCHAR(20)       NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('Draft')
                                              CONSTRAINT CK_Orders_Status CHECK (Status IN
                                              ('Draft','PendingL1','PendingL2','Approved','Rejected',
                                               'Despatched','Posted','Failed','Cancelled')),
    SubTotal       DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Orders_SubTotal  DEFAULT (0),
    TaxAmount      DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Orders_TaxAmount DEFAULT (0),
    TotalAmount    AS (SubTotal + TaxAmount) PERSISTED,
    Remarks        NVARCHAR(500)     NULL,
    IdempotencyKey VARCHAR(64)       NULL,
    CreatedBy      NVARCHAR(100)     NOT NULL CONSTRAINT DF_Orders_CreatedBy DEFAULT (N'system'),
    CreatedOn      DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_CreatedOn DEFAULT (SYSUTCDATETIME()),
    ModifiedBy     NVARCHAR(100)     NULL,
    ModifiedOn     DATETIME2(3)      NULL,
    IsDeleted      BIT               NOT NULL CONSTRAINT DF_Orders_IsDeleted DEFAULT (0),
    RowVersion     ROWVERSION        NOT NULL
);
/* Covering index for "latest N orders per customer" (CROSS APPLY) and the
   customer order history screen: seek on CustomerId, already sorted by date. */
CREATE NONCLUSTERED INDEX IX_Orders_Customer_Date
    ON dbo.Orders (CustomerId, OrderDate DESC)
    INCLUDE (OrderNo, Status, TotalAmount)
    WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Orders_Status_Date
    ON dbo.Orders (Status, OrderDate)
    INCLUDE (CustomerId, TotalAmount)
    WHERE IsDeleted = 0;
/* A retried POST with the same Idempotency-Key can never create a second order */
CREATE UNIQUE NONCLUSTERED INDEX UX_Orders_IdempotencyKey
    ON dbo.Orders (IdempotencyKey)
    WHERE IdempotencyKey IS NOT NULL;

CREATE TABLE dbo.OrderLines
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY CLUSTERED,
    OrderId     INT               NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    LineNumber  SMALLINT          NOT NULL,
    ItemId      INT               NOT NULL CONSTRAINT FK_OrderLines_Items REFERENCES dbo.Items(Id),
    Qty         DECIMAL(18,3)     NOT NULL CONSTRAINT CK_OrderLines_Qty CHECK (Qty > 0),
    UnitPrice   DECIMAL(18,2)     NOT NULL CONSTRAINT CK_OrderLines_UnitPrice CHECK (UnitPrice >= 0),
    DiscountPct DECIMAL(5,2)      NOT NULL CONSTRAINT DF_OrderLines_DiscountPct DEFAULT (0)
                                           CONSTRAINT CK_OrderLines_DiscountPct CHECK (DiscountPct BETWEEN 0 AND 100),
    TaxPercent  DECIMAL(5,2)      NOT NULL CONSTRAINT DF_OrderLines_TaxPercent DEFAULT (18),
    LineTotal   AS (CAST(Qty * UnitPrice * (100 - DiscountPct) / 100 AS DECIMAL(18,2))) PERSISTED,
    CONSTRAINT UQ_OrderLines_Order_LineNumber UNIQUE (OrderId, LineNumber)
);
CREATE NONCLUSTERED INDEX IX_OrderLines_ItemId ON dbo.OrderLines (ItemId) INCLUDE (Qty, UnitPrice);

CREATE TABLE dbo.ApprovalHistory
(
    Id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApprovalHistory PRIMARY KEY CLUSTERED,
    OrderId   INT            NOT NULL CONSTRAINT FK_ApprovalHistory_Orders REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    Level     TINYINT        NOT NULL CONSTRAINT CK_ApprovalHistory_Level CHECK (Level BETWEEN 0 AND 2),  -- 0 = submit
    Action    VARCHAR(10)    NOT NULL CONSTRAINT CK_ApprovalHistory_Action CHECK (Action IN ('Submitted','Approved','Rejected')),
    ActionBy  INT            NOT NULL CONSTRAINT FK_ApprovalHistory_Users REFERENCES dbo.Users(Id),
    ActionOn  DATETIME2(3)   NOT NULL CONSTRAINT DF_ApprovalHistory_ActionOn DEFAULT (SYSUTCDATETIME()),
    Comments  NVARCHAR(500)  NULL
);
CREATE NONCLUSTERED INDEX IX_ApprovalHistory_OrderId ON dbo.ApprovalHistory (OrderId);

/* ---------------------------------------------------------------------
   Infrastructure tables: audit log, outbox, idempotency, price import
   --------------------------------------------------------------------- */
CREATE TABLE dbo.AuditLog
(
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED,
    TableName  SYSNAME        NOT NULL,
    RecordId   NVARCHAR(50)   NOT NULL,
    Action     CHAR(1)        NOT NULL CONSTRAINT CK_AuditLog_Action CHECK (Action IN ('I','U','D')),
    OldValues  NVARCHAR(MAX)  NULL,     -- JSON
    NewValues  NVARCHAR(MAX)  NULL,     -- JSON
    ChangedBy  NVARCHAR(100)  NOT NULL,
    ChangedOn  DATETIME2(3)   NOT NULL CONSTRAINT DF_AuditLog_ChangedOn DEFAULT (SYSUTCDATETIME()),
    Source     VARCHAR(20)    NOT NULL CONSTRAINT DF_AuditLog_Source DEFAULT ('Trigger')  -- Trigger | Api
);
CREATE NONCLUSTERED INDEX IX_AuditLog_Table_Record ON dbo.AuditLog (TableName, RecordId);

/* Transactional OUTBOX: the despatch procedure writes the business change and
   the 'OrderDespatched' event in ONE transaction; a background worker
   publishes unprocessed rows to RabbitMQ (Phase 6). */
CREATE TABLE dbo.OutboxMessages
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY CLUSTERED
                                          CONSTRAINT DF_OutboxMessages_Id DEFAULT (NEWSEQUENTIALID()),
    MessageType VARCHAR(200)     NOT NULL,
    Payload     NVARCHAR(MAX)    NOT NULL CONSTRAINT CK_OutboxMessages_Payload_IsJson CHECK (ISJSON(Payload) = 1),
    OccurredOn  DATETIME2(3)     NOT NULL CONSTRAINT DF_OutboxMessages_OccurredOn DEFAULT (SYSUTCDATETIME()),
    ProcessedOn DATETIME2(3)     NULL,
    Attempts    INT              NOT NULL CONSTRAINT DF_OutboxMessages_Attempts DEFAULT (0),
    LastError   NVARCHAR(1000)   NULL
);
CREATE NONCLUSTERED INDEX IX_OutboxMessages_Pending
    ON dbo.OutboxMessages (OccurredOn)
    WHERE ProcessedOn IS NULL;                 -- the worker only ever reads pending rows

/* Stores the first response for an Idempotency-Key so a retried POST returns
   the same result instead of doing the work twice. */
CREATE TABLE dbo.IdempotencyRecords
(
    IdempotencyKey VARCHAR(64)    NOT NULL CONSTRAINT PK_IdempotencyRecords PRIMARY KEY CLUSTERED,
    RequestPath    NVARCHAR(200)  NOT NULL,
    RequestHash    VARCHAR(64)    NOT NULL,     -- SHA-256 of the body: same key + different body = 422
    StatusCode     INT            NOT NULL,
    ResponseBody   NVARCHAR(MAX)  NULL,
    CreatedOn      DATETIME2(3)   NOT NULL CONSTRAINT DF_IdempotencyRecords_CreatedOn DEFAULT (SYSUTCDATETIME())
);

/* Staging table for the supplier price file; synced into Items with MERGE */
CREATE TABLE dbo.ItemPriceImport
(
    Code       VARCHAR(30)    NOT NULL CONSTRAINT PK_ItemPriceImport PRIMARY KEY,
    Name       NVARCHAR(150)  NOT NULL,
    Category   NVARCHAR(100)  NOT NULL,
    UnitPrice  DECIMAL(18,2)  NOT NULL,
    ImportedOn DATETIME2(3)   NOT NULL CONSTRAINT DF_ItemPriceImport_ImportedOn DEFAULT (SYSUTCDATETIME())
);

/* Reporting fact table for the dashboard. A NON-CLUSTERED COLUMNSTORE index
   stores the data column by column and compresses it, which is ideal for
   SUM/GROUP BY over many rows (analytics), not for single-row lookups. */
CREATE TABLE dbo.SalesFact
(
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesFact PRIMARY KEY CLUSTERED,
    OrderId     INT            NOT NULL,
    OrderDate   DATE           NOT NULL,
    CustomerId  INT            NOT NULL,
    ItemId      INT            NOT NULL,
    CategoryId  INT            NOT NULL,
    WarehouseId INT            NOT NULL,
    Qty         DECIMAL(18,3)  NOT NULL,
    NetAmount   DECIMAL(18,2)  NOT NULL
);
CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_SalesFact
    ON dbo.SalesFact (OrderDate, CustomerId, ItemId, CategoryId, WarehouseId, Qty, NetAmount);
GO

/* =====================================================================
   Normalisation note (interview: 1NF / 2NF / 3NF)
   - 1NF: every column is atomic. Order lines live in OrderLines, not as a
          comma-separated list in Orders.
   - 2NF: no partial dependency on part of a composite key. In
          RolePermissions (RoleId, PermissionId) there are no extra columns;
          the permission's description lives in Permissions.
   - 3NF: no transitive dependency. Orders stores CustomerId, not the
          customer's city; city depends on the customer, not the order.
   - Deliberate denormalisation: StockBalances repeats SUM(StockLedger.Qty)
     so stock checks are a single-row seek; both are updated in the same
     transaction (usp_DespatchOrder). SalesFact is a reporting copy.
   ===================================================================== */
PRINT 'ErpDb schema created.';
GO
