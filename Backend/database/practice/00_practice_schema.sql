/* =====================================================================
   practice/00_practice_schema.sql
   Small, hand-made data set for the classic interview SQL questions.
   The data is chosen to expose the traps interviewers like:
     - salary ties (RANK vs DENSE_RANK vs ROW_NUMBER)
     - employees earning more than their manager (Neha > Rohan, Pooja > Arjun)
     - a department with NO employees (Legal)
     - an employee with NO department (the CEO) -> LEFT/RIGHT/FULL JOIN
     - a NULL salary (contractor) -> aggregates ignore NULL
     - NULL ManagerId (CEO) -> the NOT IN + NULL trap
     - duplicate customer e-mails -> delete duplicates
   ===================================================================== */
SET NOCOUNT ON;
USE PracticeDb;
GO

DROP TABLE IF EXISTS dbo.Requests, dbo.Sales, dbo.Customers, dbo.Employees, dbo.Departments;
GO

CREATE TABLE dbo.Departments
(
    Id   INT          NOT NULL PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE dbo.Employees
(
    Id        INT           NOT NULL PRIMARY KEY,
    Name      NVARCHAR(100) NOT NULL,
    Email     VARCHAR(100)  NOT NULL,
    DeptId    INT           NULL REFERENCES dbo.Departments(Id),
    ManagerId INT           NULL REFERENCES dbo.Employees(Id),
    Salary    DECIMAL(12,2) NULL,             -- NULL = contractor paid elsewhere
    HireDate  DATE          NOT NULL
);

CREATE TABLE dbo.Customers
(
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(100) NOT NULL,
    Email     VARCHAR(100)  NOT NULL,
    City      NVARCHAR(50)  NULL,
    CreatedOn DATETIME2(0)  NOT NULL
);

CREATE TABLE dbo.Sales
(
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    SaleDate   DATE          NOT NULL,
    CustomerId INT           NOT NULL,
    Region     VARCHAR(10)   NOT NULL,
    Amount     DECIMAL(12,2) NOT NULL
);

CREATE TABLE dbo.Requests
(
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    DeptId      INT          NOT NULL REFERENCES dbo.Departments(Id),
    Status      VARCHAR(10)  NOT NULL CHECK (Status IN ('Approved','Rejected','Pending')),
    RequestDate DATE         NOT NULL
);
GO

INSERT INTO dbo.Departments (Id, Name) VALUES
 (1, N'Engineering'), (2, N'Sales'), (3, N'Finance'), (4, N'HR'), (5, N'Legal');   -- Legal: no employees

INSERT INTO dbo.Employees (Id, Name, Email, DeptId, ManagerId, Salary, HireDate) VALUES
 ( 1, N'Ravi Sharma',    'ravi@corp.example',    NULL, NULL, 250000, '2015-04-01'),  -- CEO: no department, no manager
 ( 2, N'Priya Nair',     'priya@corp.example',   1,    1,    180000, '2016-06-15'),
 ( 3, N'Arjun Mehta',    'arjun@corp.example',   2,    1,    160000, '2016-09-01'),
 ( 4, N'Kavita Rao',     'kavita@corp.example',  3,    1,    150000, '2017-01-10'),
 ( 5, N'Rohan Gupta',    'rohan@corp.example',   1,    2,    120000, '2018-03-12'),
 ( 6, N'Sneha Iyer',     'sneha@corp.example',   1,    2,    120000, '2018-07-23'),  -- ties with Rohan
 ( 7, N'Amit Verma',     'amit@corp.example',    1,    5,     95000, '2020-02-03'),
 ( 8, N'Neha Kapoor',    'neha@corp.example',    1,    5,    130000, '2019-11-18'),  -- earns MORE than manager Rohan
 ( 9, N'Farhan Ali',     'farhan@corp.example',  2,    3,     90000, '2021-05-05'),
 (10, N'Pooja Singh',    'pooja@corp.example',   2,    3,    170000, '2017-08-30'),  -- earns MORE than manager Arjun
 (11, N'Deepak Joshi',   'deepak@corp.example',  2,    3,     90000, '2022-01-17'),  -- ties with Farhan
 (12, N'Meera Pillai',   'meera@corp.example',   3,    4,     85000, '2021-09-09'),
 (13, N'Karan Malhotra', 'karan@corp.example',   4,    1,    110000, '2019-04-22'),
 (14, N'Ananya Das',     'ananya@corp.example',  4,    13,    70000, '2023-06-01'),
 (15, N'Vivek Kumar',    'vivek@corp.example',   3,    4,     85000, '2022-10-10'),  -- ties with Meera
 (16, N'Ishaan Bose',    'ishaan@corp.example',  1,    2,      NULL, '2024-02-19');  -- contractor: NULL salary

INSERT INTO dbo.Customers (Name, Email, City, CreatedOn) VALUES
 (N'Shree Traders',      'shree@example.com',  N'Mumbai', '2025-01-05 10:00'),
 (N'Shree Traders',      'shree@example.com',  N'Mumbai', '2025-01-05 10:02'),   -- double click duplicate
 (N'Om Electricals',     'om@example.com',     N'Thane',  '2025-02-11 09:30'),
 (N'Patel Hardware',     'patel@example.com',  N'Pune',   '2025-03-01 12:00'),
 (N'Patel Hardware Co.', 'PATEL@example.com',  N'Pune',   '2025-04-15 16:45'),   -- same e-mail, different case
 (N'Konkan Builders',    'konkan@example.com', N'Ratnagiri', '2025-05-20 11:11'),
 (N'Om Electricals',     'om@example.com',     N'Thane',  '2025-06-02 08:00'),
 (N'City Plumbing',      'city@example.com',   NULL,      '2025-07-07 07:07');

/* ~22 months of sales, deterministic */
;WITH N AS (SELECT TOP (400) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO dbo.Sales (SaleDate, CustomerId, Region, Amount)
SELECT DATEADD(DAY, (n * 53) % 670, '2025-01-01'),
       n % 8 + 1,
       CHOOSE(n % 3 + 1, 'West', 'North', 'South'),
       CAST(1000 + (n * 7919) % 49000 AS DECIMAL(12,2))
FROM N;

;WITH N AS (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO dbo.Requests (DeptId, Status, RequestDate)
SELECT n % 4 + 1,                                          -- departments 1..4 (Legal raises none)
       CHOOSE(n % 5 + 1, 'Approved', 'Approved', 'Rejected', 'Pending', 'Approved'),
       DATEADD(DAY, n * 5, '2026-01-01')
FROM N;
GO

PRINT 'PracticeDb ready.';
GO
