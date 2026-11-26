/* =====================================================================
   05_app_login.sql  -  least-privilege login for the API

   The API never connects as 'sa'. minierp_app can read/write data and
   execute procedures, but cannot create, alter or drop anything, so a
   SQL-injection bug could not drop a table.
   (Guide: "give the app's login minimal permissions")

   $(AppPassword) is a sqlcmd variable; run-all.ps1 passes it from
   SQL_APP_PASSWORD in the repo-root .env.
   ===================================================================== */
SET NOCOUNT ON;
USE master;
GO
IF SUSER_ID(N'minierp_app') IS NULL
    CREATE LOGIN minierp_app WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = ErpDb;
ELSE
    ALTER LOGIN minierp_app WITH PASSWORD = N'$(AppPassword)';
GO

USE ErpDb;
GO
IF DATABASE_PRINCIPAL_ID(N'minierp_app') IS NULL
    CREATE USER minierp_app FOR LOGIN minierp_app;

ALTER ROLE db_datareader ADD MEMBER minierp_app;      -- SELECT on every table/view
ALTER ROLE db_datawriter ADD MEMBER minierp_app;      -- INSERT/UPDATE/DELETE on every table
GRANT EXECUTE ON SCHEMA::dbo TO minierp_app;          -- stored procedures + functions
GRANT UPDATE ON OBJECT::dbo.OrderNoSeq TO minierp_app; -- NEXT VALUE FOR needs UPDATE on the sequence
GO

PRINT 'Login minierp_app ready (data read/write + EXECUTE only, no DDL).';
GO
