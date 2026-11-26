# MiniErp database (SQL Server 2022)

Two databases, both created by `run-all.ps1`:

| Database | Purpose |
|---|---|
| **ErpDb** | The application database: security (RBAC), masters, stock, orders, approvals, audit, outbox |
| **PracticeDb** | A small hand-made data set for practising classic SQL interview questions, plus a tuning lab |

## Run

```powershell
docker compose up -d sqlserver                       # from the repo root
./Backend/database/run-all.ps1                       # drop + recreate both DBs, run everything
./Backend/database/run-all.ps1 -SkipPractice         # ErpDb only
./Backend/database/run-all.ps1 -File practice/03_nth_highest_salary.sql   # one script, no reset
```

The script reads `SQL_SA_PASSWORD` from the repo-root `.env` and passes it to `sqlcmd` via the
`SQLCMDPASSWORD` environment variable (never on the command line).

Connect from SSMS / Azure Data Studio: server `localhost,14333`, login `sa`, password from `.env`,
tick *Trust server certificate*.

## ErpDb scripts

| File | What it creates |
|---|---|
| [00_create_databases.sql](00_create_databases.sql) | Both databases; READ_COMMITTED_SNAPSHOT on ErpDb |
| [01_schema.sql](01_schema.sql) | 20 tables: keys, CHECK/DEFAULT/UNIQUE, cascades, `rowversion`, SEQUENCE, temporal `Items`, clustered / covering / filtered / unique-filtered / columnstore indexes, normalisation notes |
| [02_seed.sql](02_seed.sql) | Roles, permissions, 5 demo users, items, customers, 12 months of orders, stock ledger |
| [03_procs_functions.sql](03_procs_functions.sql) | `usp_DespatchOrder` (ACID transaction + outbox), `usp_ReverseDespatch` (saga compensation), `usp_GetItemsPaged` (OFFSET/FETCH), running stock, LAG report, CROSS/OUTER APPLY, scalar vs inline TVF |
| [04_views_triggers_merge.sql](04_views_triggers_merge.sql) | Views, an indexed view, the audit trigger (JSON + SESSION_CONTEXT), MERGE price sync |

### Demo logins (password `Demo@123`)

| User | Role | Can |
|---|---|---|
| `admin` | Admin | everything |
| `manager1` | Manager | masters, level-2 approval |
| `approver1` | Approver | level-1 approval |
| `store1` | StoreKeeper | create + despatch orders, adjust stock |
| `viewer1` | Viewer | read only |

### Order workflow

```
Draft -> PendingL1 -> PendingL2 -> Approved -> Despatched -> Posted
              \-> Rejected                          \-> Failed (saga compensation)
```

`usp_DespatchOrder` raises custom errors that the API maps to HTTP status codes:
`50404` → 404, `50409` → 409 (wrong status), `50422` → 422 (insufficient stock).

## Practice scripts (PracticeDb)

| # | Topic |
|---|---|
| 01 | INNER / LEFT / RIGHT / FULL / CROSS / SELF joins, ON vs WHERE trap |
| 02 | WHERE vs HAVING, logical query order, aggregates and NULL |
| 03 | Nth highest salary; ROW_NUMBER vs RANK vs DENSE_RANK |
| 04 | Find and delete duplicates (CTE + ROW_NUMBER) |
| 05 | Running totals, moving averages, ROWS vs RANGE |
| 06 | Recursive CTE: org chart, management chain |
| 07 | LAG / LEAD month-over-month |
| 08 | Anti-join: LEFT JOIN IS NULL / NOT EXISTS / EXCEPT |
| 09 | OFFSET/FETCH vs keyset pagination |
| 10 | Self join: earns more than manager |
| 11 | Top N per group |
| 12 | Pivot (CASE and PIVOT), UNPIVOT |
| 13 | IN vs EXISTS, NOT IN + NULL trap |
| 14 | Scalar, correlated and derived-table subqueries, ALL |
| 15 | DDL / DML / DCL / TCL, savepoints, DELETE vs TRUNCATE vs DROP, UNION vs UNION ALL |
| 16 | Temp table vs table variable vs CTE; CHAR / VARCHAR / NVARCHAR |
| 17 | CROSS / OUTER APPLY, MERGE, temporal queries, SESSION_CONTEXT |
| 18 | Procedure vs function, scalar vs inline TVF |
| 19 | Isolation levels, NOLOCK, snapshot *(manual, two windows)* |
| 20 | Deadlock repro (error 1205) and fixes *(manual, two windows)* |
| [tuning-lab](practice/tuning-lab.sql) | SARGable predicates, implicit conversion, key lookup vs covering index, SELECT *, parameter sniffing, statistics, fragmentation, all measured in logical reads |

Sample tuning-lab results (logical reads, lower is better):

| Test | Before | After |
|---|---:|---:|
| `YEAR(OrderDate) = 2024` → date range | 468 | 177 |
| `Status = N'Pending'` (nvarchar) → `'Pending'` | 500 | 3 |
| Seek + key lookup → covering index | 185 | 28 |
| `SELECT *` → only needed columns | 14,356 | 365 |
| Parameter sniffing → `OPTION (RECOMPILE)` | 637,373 | 1,404 |
