# Kidev

**Kidev** is the Georgian word **"კიდევ"**, meaning **"again"**. Depending on context, it can also mean "more" or "still".

Kidev is a modern, simple, and fast .NET background-job platform inspired by Hangfire.

## Status

> [!WARNING]
> Kidev is in active development and is not ready for production use.

## The Idea

Register a service method once during application setup. Kidev captures its durable definition, stores it in PostgreSQL, and will execute it on its cron schedule.

```csharp
builder.Services
    .AddKidev(kidev =>
    {
        kidev.Run<IEmailService>("send-digest", service => service.SendDigest("weekly", 25))
            .EveryMinute(5);
    })
    .AddPostgreSqlStorage(connectionString);
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
- Database synchronization, lease-based claiming, execution history, and an embedded MVC dashboard RCL are implemented. The project remains pre-release.

See [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) for the current architecture and MVP scope.

## Embedded Dashboard

`Kidev.Dashboard` is a .NET 10 Razor Class Library, not a runnable application. Reference its NuGet package from your ASP.NET Core MVC host. The host owns startup, storage, authentication, middleware, migrations, bootstrap, and any workers. `AddKidevDashboard()` registers MVC parts and the `dashboard-access` Administrator policy only.

This host uses optional local Identity and read-only job monitoring. A worker host can replace `AddKidevDashboardStorage` with `AddKidev(...).AddPostgreSqlStorage(connectionString)`, which registers dashboard queries too.

```csharp
using Kidev.Dashboard;
using Kidev.Dashboard.Identity;
using Kidev.Storage.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Kidev")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Kidev.");
const string prefix = "/kidev";
builder.Services.AddControllersWithViews();
builder.Services.AddKidevDashboardStorage(connectionString);
builder.Services.AddKidevDashboard();
builder.Services.AddKidevDashboardIdentity(connectionString);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Kidev.Dashboard.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = prefix + "/Account/Login";
    options.AccessDeniedPath = prefix + "/Account/Denied";
});

await using var app = builder.Build();
if (args is ["setup", "--migrate-identity"])
{
    await using var scope = app.Services.CreateAsyncScope();
    Environment.ExitCode = await scope.ServiceProvider
        .GetRequiredService<AdministratorSetupService>()
        .RunAsync(migrateIdentity: true, app.Lifetime.ApplicationStopping);
    return; // Never start the web host or workers during setup.
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(prefix + "/Account/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next(context);
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapKidevDashboard(prefix);
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
await app.RunAsync();
```

Map the dashboard before the host's default MVC route. The isolated `Kidev` area prevents collisions with host `Home`, `Account`, or `Dashboard` controllers. Nested prefixes such as `/operations/jobs` are supported; configure cookie paths to match. For host navigation use `<a asp-area="Kidev" asp-controller="Dashboard" asp-action="Index">Jobs</a>`. Dashboard views are compiled from `Areas/Kidev/Views`; versioned static assets use `/_content/Kidev.Dashboard/...`. No dashboard controllers, views, or assets need copying into the host. In development, ASP.NET Core loads referenced static web assets automatically; `MapStaticAssets` serves packaged assets from a published host too.

`AddKidevDashboardIdentity` is opt-in and does not migrate or create accounts. Hosts using another authentication system must supply an authenticated Administrator principal for operational pages; the packaged Account pages require the local `DashboardUser` Identity services. Keep authentication and authorization middleware enabled. MVC antiforgery, safe database errors, and no-store result filters are scoped to dashboard controllers; the prefix-scoped host middleware above also covers authentication challenges without changing host pages.

### Migrations And Bootstrap

Apply existing job migrations through deployment tooling. From this repository, set `KIDEV_POSTGRES_CONNECTION_STRING` securely and use a consuming host with the EF Design tooling package as startup project:

```bash
dotnet ef database update --project Kidev.Storage.PostgreSQL --startup-project path/to/YourHost --context KidevDbContext
dotnet run --project path/to/YourHost -- setup --migrate-identity
```

Setup prompts for name, email, password, and confirmation, with hidden password input. Use at least 12 characters with uppercase, lowercase, a digit, and a symbol. Setup refuses another first administrator once accounts exist. It applies only the separate Identity migrations in schema `kidev_dashboard`, using `__IdentityMigrationsHistory`; normal startup never migrates or bootstraps.

For unattended setup, provide `KIDEV_DASHBOARD_ADMIN_NAME`, `KIDEV_DASHBOARD_ADMIN_EMAIL`, and `KIDEV_DASHBOARD_ADMIN_PASSWORD` through a secret manager, then remove them from the web process. Subsequent Identity updates do not require bootstrap:

```bash
dotnet ef database update --project Kidev.Dashboard --startup-project path/to/YourHost --context DashboardIdentityContext
```

Sign in at `/kidev`, then use **Settings -> Create account** to add another Administrator. There is no public registration. Job pages are read-only; payloads and connection credentials are hidden. Workers shows recorded active leases, not a server registry; metrics are limited to stored history.

The **Visualization** page at `/kidev/Visualization` shows live process, worker, and job activity using the factory metaphor. Its administrator-only `/kidev/Visualization/Snapshot` endpoint provides bounded snapshots approximately every 2 seconds. Process heartbeats run every 10 seconds; contact older than 30 seconds is shown as Stale. Both routes follow your configured dashboard prefix; no former Factory route alias is provided.

For production, persist and protect Data Protection keys, provision HTTPS, and restrict database permissions. Dashboard operations need job-table reads and Identity account writes; migrations should use deployment credentials.

### Local MVC Consumer

Local testing belongs in ignored `Kidev.TestApp/`, outside the solution. The local demo uses only PackageReferences to matching timestamp-versioned Core, Storage, and Dashboard packages in `Kidev.TestApp/feed`, its own PostgreSQL 17 database, and an explicit setup command. Its `README.md` documents bootstrap, package refresh, run/stop, and credential paths. Do not commit the demo or its credentials, data, feed, or logs.

## Testing

`dotnet test Kidev.slnx` includes Core tests, MVC/security tests, and PostgreSQL integration tests. Start Docker before running the full suite; Testcontainers provisions disposable PostgreSQL instances and applies real migrations. CI runs the new dashboard project as part of the solution and collects coverage.

See [DESIGN.md](DESIGN.md) for the finalized design system and the longer-term dashboard brief.
