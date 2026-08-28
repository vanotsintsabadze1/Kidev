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

- Recurring or scheduled jobs.
- Distributed worker coordination beyond the initial persistence-backed design.
- Retries, queues with priorities, rate limits, and job continuations.
- A public compatibility layer for Hangfire.
- Advanced dashboard filtering, metrics, or administration.

## Architecture Mind Map

```text
Kidev
|
|-- Kidev.Core
|   |-- Job contracts and domain models
|   |-- Job execution abstractions
|   `-- KidevRunner
|       `-- Hosted BackgroundService; remains active until host cancellation
|
|-- Kidev.Storage.PostgreSQL
|   |-- PostgreSQL persistence implementation
|   `-- Job state and execution records
|
`-- Kidev.Dashboard
    |-- Job status visibility
    `-- Basic operational controls

MVP flow
Submit job -> Persist job -> Runner claims job -> Execute job
    -> Persist completion or failure -> Dashboard displays status
```

## Current State

- `Kidev.Core` contains the initial `KidevRunner`, a hosted service that waits for host cancellation. It does not execute jobs yet.
- `Kidev.Storage.PostgreSQL` is an empty storage project scaffold.
- `Kidev.Dashboard` is an empty Razor component library scaffold.
- No job contract, persistence schema, job execution, hosting application, or dashboard job-status UI exists yet.

## Implementation Direction

Build vertically in MVP-sized increments. Add a job contract and state model first, then persistence, runner claiming/execution, and finally dashboard visibility. Keep interfaces limited to persistence and hosting boundaries where substitution is required.
