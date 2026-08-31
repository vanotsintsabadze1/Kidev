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
    |-- Job status visibility
    `-- Basic operational controls

MVP flow
Submit job -> Persist job -> Runner claims job -> Execute job
    -> Persist completion or failure -> Dashboard displays status
```

## Current State

- `Kidev.Core` contains `KidevRunner`, a hosted service that retrieves due jobs through the persistence boundary and invokes their registered service methods through DI using reflection.
- `Kidev.Core/Data/JobDefinition` persists a recurring job's generated integer ID, service assembly/type, method, cron expression, UTC-default time zone, execution timestamps, and enabled state.
- Applications register direct service method calls through `services.AddKidev(...)`. Registrations use explicit stable keys, allow constant arguments only, and are frozen into an internal catalog after application setup.
- Enabled jobs are retrieved through a PostgreSQL partial index ordered by next execution time and ID; cron expressions are parsed only when calculating the next occurrence.
- `Kidev.Storage.PostgreSQL` keeps its EF Core context and due-job store internal. Applications configure the storage through `services.AddPostgreSqlStorage(connectionString)`; its schema migration and model snapshot ship under `Migrations`.
- `Kidev.Dashboard` is an empty Razor component library scaffold.
- No database synchronization, next-execution updates, hosting application, or dashboard job-status UI exists yet.

## Implementation Direction

Build vertically in MVP-sized increments. Add database synchronization next, then runner claiming/execution, and finally dashboard visibility. Keep interfaces limited to persistence and hosting boundaries where substitution is required.
