# Kidev

**Kidev** is the Georgian word **"კიდევ"**, meaning **"again"**. Depending on context, it can also mean "more" or "still".

Kidev is a modern, simple, and fast .NET background-job platform inspired by Hangfire.

## Status

> [!WARNING]
> Kidev is in active development and is not ready for production use.

## The Idea

Register a service method once during application setup. Kidev captures its durable definition, stores it in PostgreSQL, and will execute it on its cron schedule.

```csharp
builder.Services.AddKidev(kidev =>
{
    kidev.Run<IEmailService>("send-digest", service => service.SendDigest("weekly", 25))
        .EveryMinute(5);
});
```

The registration above describes a job. It does not execute the method during application setup.

```text
Application setup
    |
    v
Kidev inspects the service method call
    |
    v
Stores the job definition and cron schedule in PostgreSQL
    |
    v
Runner claims due jobs and invokes the service through DI
    |
    v
Dashboard shows job status
```

## Job Definition

Kidev persists the information needed to execute a job later:

| Value | Example |
| --- | --- |
| Stable registration key | `send-digest` |
| Service type and assembly | `IEmailService` |
| Method and parameter types | `SendDigest(string, int)` |
| Argument payload | `"weekly", 25` |
| Cron schedule | `*/5 * * * *` |
| Time zone | `UTC` by default |
| Execution state | Last and next execution timestamps; enabled state |

Only direct service method calls with constant arguments are currently supported. This keeps the stored job definition predictable and safe to serialize.

## MVP Direction

- Register service method calls during application setup.
- Persist job definitions in PostgreSQL.
- Schedule jobs with cron expressions in UTC by default.
- Synchronize registered jobs to the database at host startup.
- Execute due jobs through hosted services.
- Provide basic job visibility through a dashboard.

## Current State

- Build-time registration, cron builders, PostgreSQL entity mapping, and the initial EF Core migration exist.
- Database synchronization, due-job execution, and dashboard views are still being built.

See [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) for the current architecture and MVP scope.
