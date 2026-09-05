# Kidev Project Overview

## Purpose

Kidev is a simpler, faster, modern background-job platform inspired by Hangfire. It will run durable background work while providing visibility and control through a dashboard.

This document is the living overview of the product and architecture. Update it whenever project goals, MVP scope, architecture, or component responsibilities change.

## MVP Goal

Deliver the smallest useful system that can:

1. Accept a background job.
2. Persist the job in PostgreSQL.
3. Execute the job in a hosted runner.
4. Record whether the job completed or failed.
5. Show basic job status in a dashboard.

## Non-Goals For The MVP

- Advanced schedules beyond the initial minute and hour recurrence builders.
- Distributed worker coordination beyond the initial persistence-backed design.
- Retries, queues with priorities, rate limits, and job continuations.
- A public compatibility layer for Hangfire.
- Advanced dashboard filtering, metrics, or administration.

## Architecture Mind Map

```text
Kidev
|
|-- Kidev.Core
|   |-- Job contracts, domain models, and persistence entities
|   |-- Public build-time job registration API
|   |-- Internal immutable registration catalog
|   |-- Job execution abstractions
|   `-- KidevRunner
|       `-- Hosted BackgroundService; retrieves and invokes due jobs
|
|-- Kidev.Storage.PostgreSQL
|   |-- Npgsql Entity Framework Core database context
|   `-- PostgreSQL mapping for core persistence entities
|       `-- Versioned EF Core migrations
|
`-- Kidev.Dashboard
    |-- Razor Class Library plugin with compiled, area-isolated MVC views and static assets
    |-- Core dashboard query boundary implemented by PostgreSQL
    `-- Optional local Identity accounts and explicit setup in a separate schema

MVP flow
Submit job -> Persist job -> Runner claims job -> Execute job
    -> Persist completion or failure -> Dashboard displays status
```

## Current State

- `Kidev.Core` contains `KidevRunner`, a hosted service that starts a configurable worker pool, atomically claims due jobs through the persistence boundary, renews their leases, and invokes registered service methods through DI using reflection.
- `Kidev.Core/Data/JobDefinition` persists a recurring job's generated integer ID, service assembly/type, method, cron expression, UTC-default time zone, execution timestamps, and enabled state.
- Applications register direct service method calls through `services.AddKidev(...)`. Registrations use explicit stable keys, allow constant arguments only, and are frozen into an internal catalog after application setup.
- Enabled jobs are claimed through a PostgreSQL partial index ordered by next execution time and ID. Claims use a worker-specific identifier and expiring lease so concurrent workers do not execute the same job while its owner is healthy.
- `Kidev.Storage.PostgreSQL` keeps its EF Core context and due-job store internal. Applications configure the storage through `services.AddPostgreSqlStorage(connectionString)`; its schema migration and model snapshot ship under `Migrations`.
- `Kidev.Dashboard` is a .NET 10 Razor Class Library embedded through `AddKidevDashboard` and `MapKidevDashboard("/kidev")`, with no production entry point. Its `Kidev` MVC area contains processing-floor overview, job search/details/history, recurring and scheduled definitions, active leases, failures, bounded metrics, and settings. Compiled views and `/_content/Kidev.Dashboard` static assets ship in the package. Reads use `IDashboardQuery`; registration never starts workers or modifies registrations.
- The consuming host owns configuration, storage, authentication, middleware, migrations, setup, and optional worker registration. `AddPostgreSqlStorage` registers both worker storage and dashboard queries; `AddKidevDashboardStorage` supports read-only monitoring without workers.
- Optional local email/password authentication is registered explicitly through `AddKidevDashboardIdentity`. Only Administrators can view operational data or create accounts; the first administrator is provisioned through a host-owned CLI setup command using `AdministratorSetupService`. Identity uses its own context, schema, migration history, and unchanged EF-generated migrations in `Kidev.Dashboard/Identity/Migrations`. The host configures cookie paths for its dashboard prefix.
- The ignored `Kidev.TestApp/` demo is an ordinary MVC NuGet consumer outside the solution, not a shipped component. It runs four workers and harmless synchronous scheduled jobs against its own local PostgreSQL database, with explicit bootstrap and no production configuration inheritance.
- Registered jobs are synchronized to PostgreSQL when `KidevRunner` starts. New jobs receive their first scheduled execution time, changed schedules are recalculated, and jobs removed from registration are disabled.
- Execution history records success, failure, heartbeat, and lease-expiry information. Dashboard pages use persisted data only; no worker registry, queue model, raw log storage, retry controls, or machine analytics is implied.

## Implementation Direction

The dashboard also includes a live Visualization page at `/kidev/Visualization`, retaining the factory metaphor. Workers register application-process identity and capacity in PostgreSQL and heartbeat every 10 seconds independently of job leases; contact older than 30 seconds is Stale. The administrator-only, read-only `/kidev/Visualization/Snapshot` endpoint returns bounded snapshots and recent transitions derived from execution history; the browser refreshes every two seconds with explicit stale/truncated states. Both routes follow the configured dashboard prefix, with no former Factory aliases. This is not a guaranteed streaming event feed. The `AddWorkerProcesses` EF-generated migration is required before running the updated workers.

Build vertically in MVP-sized increments. Harden the worker lifecycle and dashboard for release before adding queues, retries, or machine analytics. Keep interfaces limited to persistence and hosting boundaries where substitution is required. `DESIGN.md` records the finalized MVC design and distinguishes delivered features from the longer-term product brief.
