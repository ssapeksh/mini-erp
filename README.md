# MiniErp

A small ERP (Items → Orders → Stock → Ledger, with a multi-level approval workflow), built as a full-stack .NET practice project. It is intentionally small, but it touches the whole stack:

| Area | What it uses |
|---|---|
| Database | SQL Server 2022: CTEs, window functions, stored procedures, indexes, transactions, temporal tables |
| Backend | ASP.NET Core 10 Web API, EF Core, JWT + permission-based RBAC, middleware, filters, background services |
| Legacy | CoreWCF (SOAP) pricing service, NHibernate ledger service, a jQuery/MVC page |
| Frontend | Angular 21: standalone components, signals, RxJS, reactive forms, interceptors, guards |
| Architecture | YARP API gateway, RabbitMQ + MassTransit, outbox pattern, choreography saga, OpenTelemetry |
| Quality | xUnit + Moq, integration tests with Testcontainers, TDD, Jenkins pipeline, SonarQube |

## Architecture

```
Angular (4200) ──► YARP Gateway (5000)
                      ├─► Erp.Api (5101)         EF Core    · ErpDb     Items / Orders / Stock / Identity
                      └─► Ledger.Service (5201)  NHibernate · LedgerDb  Accounts / Ledger entries
Erp.Api ──SOAP──► Pricing.WcfService (5301 http / 8089 net.tcp)
Erp.Api ──Outbox → RabbitMQ: OrderDespatched ──► Ledger ──► LedgerPosted | LedgerFailed ──► Erp.Api
```

## Repository layout

```
Backend/                    all .NET + SQL code
  MiniErp.slnx              the .NET solution
  database/                 SQL scripts (schema, procs, views) + practice/ queries and a tuning lab
  src/Shared/               MiniErp.Contracts: message contracts and API response types
  src/Gateway/              YARP API gateway
  src/Erp/                  Erp.Core (domain + services), Erp.Infrastructure (EF Core, Dapper), Erp.Api
  src/Ledger/               Ledger.Service (NHibernate)
  src/Pricing/              Pricing.WcfService (CoreWCF)
  src/Katas/                C# coding problems
  tests/                    unit, integration and kata tests
Frontend/                   all client code
  erp-web/                  Angular app
docs/                       architecture, agile backlog, WCF and NHibernate notes
docker-compose.yml          local infrastructure for both sides
```

## Getting started

Prerequisites: .NET SDK 10, Node 24 (npm 11), Docker Desktop. The Angular CLI comes from the project's own dependencies (`npx ng`).

```bash
# 1. Secrets: copy the example and set your own values
cp .env.example .env

# 2. Infrastructure: SQL Server (localhost,14333) and RabbitMQ (UI on http://localhost:15672)
docker compose up -d sqlserver rabbitmq

# 3. Create ErpDb + PracticeDb and load demo data (PowerShell)
./Backend/database/run-all.ps1

# 4. Put the connection string + JWT key into dotnet user-secrets (read from .env)
./Backend/scripts/set-dev-secrets.ps1

# 5. Build and run the API -> http://localhost:5101/swagger
dotnet build Backend/MiniErp.slnx
dotnet run --project Backend/src/Erp/Erp.Api --launch-profile http
```

Try it:
- **Swagger UI**: http://localhost:5101/swagger. Log in with `POST /api/v1/auth/login`, click *Authorize*, paste the `accessToken`.
- **Request collection**: [Backend/src/Erp/Erp.Api/Erp.Api.http](Backend/src/Erp/Erp.Api/Erp.Api.http) walks through every endpoint, including the approval workflow.
- **Legacy MVC + jQuery page**: http://localhost:5101/legacy (cookie login + anti-forgery tokens).
- **JavaScript playground**: http://localhost:5101/playground/js-playground.html.
- **Health**: http://localhost:5101/health/ready.

```bash
# 6. Run the Angular client -> http://localhost:4200
cd Frontend/erp-web
npm install
npm start
```

Sign in as `store1`, `approver1`, `manager1`, `viewer1` or `admin` (password `Demo@123`): each role sees different menus and buttons.
See [Frontend/erp-web/README.md](Frontend/erp-web/README.md) for the structure and a step-by-step approval flow to try.

Demo logins (password `Demo@123`): `admin`, `manager1`, `approver1`, `store1`, `viewer1`.
See [Backend/database/README.md](Backend/database/README.md) for the schema, procedures and SQL practice scripts.

Each phase adds its own run instructions below.

## Build phases

The project is built feature by feature; each phase is one pull request.

| # | Branch | Content |
|---|---|---|
| 0 | `main` | Solution skeleton, docker-compose, repo setup |
| 1 | `feature/01-database` | Schema, stored procedures, advanced SQL practice |
| 2 | `feature/02-web-api` | C# core, EF Core, ASP.NET Core Web API, jQuery page |
| 3 | `feature/03-angular` | Angular client |
| 4 | `feature/04-testing` | Unit, integration and kata tests (TDD) |
| 5 | `feature/05-wcf-nhibernate` | CoreWCF pricing service, NHibernate ledger service |
| 6 | `feature/06-microservices` | Gateway, messaging, outbox, saga, observability, containers |
| 7 | `feature/07-ci-agile` | Jenkins pipeline, SonarQube, agile docs |
