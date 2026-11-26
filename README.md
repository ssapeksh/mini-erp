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
database/          SQL scripts (schema, procs, views) + practice/ queries and a tuning lab
src/Shared/        MiniErp.Contracts: message contracts and API response types
src/Gateway/       YARP API gateway
src/Erp/           Erp.Core (domain + services), Erp.Infrastructure (EF Core, Dapper), Erp.Api
src/Ledger/        Ledger.Service (NHibernate)
src/Pricing/       Pricing.WcfService (CoreWCF)
src/Katas/         C# coding problems
client/erp-web/    Angular app
tests/             unit, integration and kata tests
docs/              architecture, agile backlog, WCF and NHibernate notes
```

## Getting started

Prerequisites: .NET SDK 10, Node 24 + Angular CLI 21, Docker Desktop.

```bash
# 1. Secrets: copy the example and set your own values
cp .env.example .env

# 2. Infrastructure: SQL Server (localhost,14333) and RabbitMQ (UI on http://localhost:15672)
docker compose up -d sqlserver rabbitmq

# 3. Build everything
dotnet build MiniErp.slnx
```

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
