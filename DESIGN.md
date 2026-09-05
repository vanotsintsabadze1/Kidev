# Finalized Implementation Decisions

These decisions describe the implemented first dashboard and take precedence over the aspirational page list below. Extend this section when the product gains new capabilities; do not present unsupported features as live telemetry.

## Hosting And Access

* `Kidev.Dashboard` is a .NET 10 Razor Class Library plugin, never a standalone executable. Hosts register `AddKidevDashboard()` and map `MapKidevDashboard("/kidev")`.
* The consuming MVC host owns configuration, PostgreSQL storage, middleware, authentication, migrations, explicit setup, and any workers. Dashboard registration never calls `AddKidev`, synchronizes registrations, or starts workers. A host can run workers through `AddKidev(...).AddPostgreSqlStorage(...)`, or register only `AddKidevDashboardStorage(...)` for read-only monitoring.
* Controllers and compiled views are isolated in MVC area `Kidev`, with views under `Areas/Kidev/Views`. Links and forms retain the mapped prefix. Packaged static assets use versioned `/_content/Kidev.Dashboard/...` URLs; hosts do not copy dashboard views, controllers, or assets.
* Job data is read through the Core `IDashboardQuery` boundary implemented by PostgreSQL. Controllers do not use the worker store for dashboard reads.
* Opt-in `AddKidevDashboardIdentity(connectionString)` provides local email/password accounts in the separate `kidev_dashboard` schema. Identity has its own context and migration-history table; worker packages do not depend on Identity. Hosts configure secure cookies and login/denied paths for their dashboard prefix.
* The first administrator is created through an explicit setup command. Registration is administrator-only and creates another administrator; anonymous signup is not available.
* All operational views require the dashboard Administrator policy. POST forms use antiforgery tokens. Errors, argument payloads, passwords, and connection strings must not leak into unauthenticated responses.
* Normal startup does not apply migrations or create accounts. Job migrations are managed separately from Identity setup.
* Local testing uses one ignored `Kidev.TestApp/` MVC host outside the solution, consuming actual local NuGet packages through PackageReferences and using an isolated PostgreSQL database. It is not a shipped dashboard executable.

## Visual System

* Dark-first graphite: background `#17191c`, surface `#202328`, raised surface `#272b30`, text `#e4e7eb`. Light mode uses the same semantic CSS tokens with background `#f5f6f7` and text `#242b33`.
* Theme choice is browser-local and persistent when local storage is available. No external font, JavaScript, or CSS CDN is required.
* Typography uses native system sans-serif at 13px; metadata is 11px, supporting text 12px, section titles 16px, and page titles 22px. Monospace is reserved for identifiers, numbers, code, and UTC timestamps.
* Layout uses a 4px spacing grid, 228px desktop sidebar, 32px controls, 40px base rows, 4px control corners, and 6px panel corners.
* Status colors are restrained: blue for active leases, purple for scheduling, green for success, red for failure, amber for lease expiry. Status text always accompanies color.
* The processing floor is the signature component: Registered -> Due -> Running -> Succeeded, with failure and lease-expiry branches. Connections describe the lifecycle, not simulated job movement.
* Use official Lucide icons at 16px with consistent strokes and `currentColor`, alongside visible navigation and control labels. The allowlisted Razor icon partial loads a packaged local SVG sprite; source attribution and ISC/MIT notices ship alongside it. Icons are decorative to assistive technology when labels already describe their purpose. No runtime icon CDN or mixed icon families.

## Pages And Data Meaning

* Implemented navigation: Overview, Visualization, Jobs, Recurring, Scheduled, Workers, Failed, Metrics, and Settings. Job details use a dedicated page, with reusable history/status partials.
* Overview and metrics use real database counts. Definition counts and execution counts are labeled separately; totals across retained history are not called throughput.
* Lists use bounded pages of 50 items. Job details show the latest 50 attempts. Definition-only and execution-only views paginate their own visible collection.
* Workers shows owners of active execution leases, not a complete server registry. Capacity, idle worker count, uptime, queue topology, and machine health cannot be inferred from this data.
* An elapsed Running lease is displayed as LeaseExpired at observation time without writing to the database. Expiry does not prove user code has stopped.
* No production mock data, invented charts, fake queues, retry controls, cancel/delete actions, stack traces, or log viewer. Those sections of the brief remain future work until their backend exists.
* Details expose recorded error summaries as encoded text. Job arguments remain hidden. Lease timestamps and execution history provide inspection without exposing payloads.

## Interaction And Accessibility

* MVC views use strongly typed models, shared layouts, and partials. Native CSS and small vanilla JavaScript files handle browser interactions; no SPA framework is used.
* Search, filters, and pagination are server-side GET requests. Forms remain usable without JavaScript.
* Manual refresh is always available; optional auto-refresh reloads operational pages every 15 seconds and pauses during relevant user interactions or when the tab is hidden. This is polling, not a streaming feed.
* Account screens disable automatic refresh. Login is a compact centered panel over a faint static processing schematic; registration stays inside the authenticated application shell.
* Empty states explain missing data. Database failures use a safe error page and HTTP 503, never fabricated zero counts.
* Support dark/light themes, visible keyboard focus, labeled forms, a skip link, responsive navigation, horizontally contained tables, and reduced-motion preferences.

---

# Live Visualization View

Implemented at `{dashboardPrefix}/Visualization` (default `/kidev/Visualization`), with an administrator-only snapshot endpoint at `{dashboardPrefix}/Visualization/Snapshot`. The public page name is Visualization; the factory metaphor remains its visual language. This page displays persisted process ownership and job activity without reloading the page. The former Factory routes have no aliases.

## Delivered Behavior

* Application instances register their name, machine, OS process ID, configured worker count, and unique per-start ID. Independent process heartbeats run every 10 seconds. Only contact newer than 30 seconds appears on the live floor. A graceful shutdown records Stopped after workers drain, subject to the host's shutdown deadline; stale and stopped process history never occupies active worker stations.
* Worker slots belong to their registered process. Jobs are attempt nodes keyed by ClaimId. Missing or historical unregistered owners are not assigned to invented processes.
* Snapshots refresh approximately every 2 seconds. Each includes a two-minute overlapping transition window derived from persisted execution start/end facts, not a separate event table. Events are deduplicated by claim and transition; inferred lease expiry is marked as inferred. This is near-real-time monitoring, not guaranteed event delivery or full replay.
* Server limits are 20 processes, 128 displayed worker slots, 100 definitions, 100 attempts, and 100 transitions. Truncation is visible; actual configured capacity is retained separately. The outcome lane displays a bounded recent subset.
* Nodes, connections, and worker stations preserve their positions across updates. Flow dashes show reported execution activity, while a heartbeat flash requires a newer stored timestamp.
* Pause/resume, manual refresh, hidden-tab suspension, request timeouts/backoff, disconnected states, keyboard-accessible inspectors, and a table alternative are implemented. Stale data stops suggesting current motion. Light theme and reduced motion are supported.
* The Visualization inspector shows safe job/claim/process/worker identifiers, timing, and generic failure/expiry summaries. It links to the existing authorized details page for stored error details; raw exception messages and arguments are not returned by the Visualization JSON endpoint.

The sections below retain design direction and longer-term work where noted; a durable streaming dispatcher and SignalR are not implemented.

## Layout And Meaning

* The dedicated Visualization page retains the compact read-only Overview for routine monitoring.
* Use a left-to-right schematic: due/scheduled job nodes, process enclosures containing worker stations, then completed/failed/expired exit lanes. Use thin directional paths, graphite surfaces, restrained status colors, and a faint alignment grid rather than decorative machinery.
* Each process enclosure represents one running application instance, not a machine or a web server request. Label it with a host-supplied display name, machine/container identity, OS process ID, unique instance ID, last heartbeat, and configured worker count.
* Worker stations stay in stable slots inside their owning process. A job-attempt node docks at its executing worker and connects to the relevant outcome lane after a confirmed state change. Display at most a bounded recent history; group overflow rather than animating hundreds of nodes.
* Identify an attempt node by ClaimId, and show the recurring definition name as its label. A recurring job's later execution is a new node, not an old completed node silently turning Running again.
* Clicking a node opens a side inspector: job/method, process/worker, attempt and claim IDs, scheduled/start/end times, elapsed time, last heartbeat, lease deadline, and encoded error summary. Do not display payloads or claim percentage progress without user-reported progress data.

## Animation Rules

* Animate only observed changes: route a newly claimed node to its worker, mark confirmed success green, divert failure red, and show lost leases amber with a dashed ownership edge.
* A slow traveling dash indicates the worker is reported busy, not a database heartbeat or job progress. A heartbeat flash occurs only when a newer persisted heartbeat is observed.
* Lease expiry means contact was lost, not that execution stopped; preserve that warning when a replacement claim appears.
* Keep node positions stable while values change. Avoid force-directed layouts that rearrange every update. Retain selection, zoom, and pan across refreshes.
* Pause animation when the tab is hidden or the user chooses Pause. Provide reduced-motion behavior, keyboard-selectable nodes, and an equivalent table view. Mark disconnected/stale data explicitly and stop suggesting current activity.

## Backend Status And Future Work

* Process registration is now persisted and worker slots are derived from registered capacity. Legacy attempts without registered owners remain explicitly unknown. Stale contact alone cannot prove a process has crashed.
* Persisted process registration and heartbeat include a unique per-start instance ID, bounded host metadata, worker count, and graceful-stop time. Worker identity is process instance plus slot; retain process records long enough to resolve retained execution history.
* Future durable streaming can persist compact lifecycle events in the same transaction as state changes. The current version derives transitions from the already transactional execution history; it does not create event rows for animation frames or heartbeats.
* The implemented administrator-only snapshot endpoint is polled approximately every 2 seconds without page reload, with a server observation timestamp. This is near-real-time; recent transitions come from persisted execution history. A future durable event feed is required before guaranteeing every transition.
* For event delivery, cursor ordering must handle transaction commit order: a raw identity value alone is not a safe high-water mark. Use an ordered dispatcher or overlap reads and deduplicate by event ID, plus periodic full snapshot reconciliation and an explicit history-gap signal.
* SignalR can later push those persisted events to browsers. Dashboard processes must read the shared database/event feed; an in-memory hub in one worker host will not see other application instances.
* Use native SVG paths and accessible HTML/SVG nodes initially; no WebGL or SPA rewrite is required. Polling must avoid overlapping requests and apply backoff after errors. Define node/event limits and retention before scaling out.

---

# Original Product Brief

You are designing and implementing the dashboard UI for a modern .NET background job scheduling library, conceptually similar to Hangfire.

The dashboard is built with ASP.NET Core MVC.

Your job is not to create a generic admin dashboard. The product should have a strong, recognizable visual identity based on the idea of observing a live factory or processing plant.

The dashboard should make background job processing feel physical and understandable.

A user should feel like they are looking inside a machine and watching work move through it.

## Product concept

The system manages background jobs.

Jobs can be:

* Scheduled
* Enqueued
* Waiting
* Claimed by a worker
* Running
* Retrying
* Completed
* Failed
* Cancelled
* Dead / exhausted retries

The system also has:

* Queues
* Workers
* Servers / nodes
* Recurring jobs
* Scheduled jobs
* Job execution history
* Logs
* Exceptions
* Retry attempts
* Execution duration
* Job payload / arguments
* Worker heartbeats
* Queue throughput
* System health

The visual language should make these concepts understandable without relying entirely on tables.

---

# Core design direction

Think:

"industrial control room for software jobs"

but interpreted through extremely polished modern product design.

Do NOT make it look like:

* a literal cartoon factory
* steampunk
* cyberpunk
* a video game
* Datadog clone
* Grafana clone
* Linear clone
* Vercel clone
* Stripe clone
* generic Tailwind admin template
* generic shadcn dashboard
* generic AI-generated SaaS interface

The factory metaphor should appear through interaction, layout, motion, diagrams, status indicators, pipelines, queue visualization, and spatial relationships.

It should remain professional enough that an engineering team would genuinely use it in production.

---

# Main visual metaphor

Treat the job system as a factory floor.

Examples:

Queues can visually resemble compact processing lanes.

Workers can resemble stations connected to those lanes.

Jobs are small units moving through the system.

Scheduled jobs are waiting upstream.

Queued jobs are waiting to enter processing.

Running jobs are currently inside a worker station.

Successful jobs leave through the completed path.

Failed jobs get visually diverted into a failure/retry path.

Retries should appear as loops back into processing.

Do this subtly.

Do not draw literal conveyor belts with cartoon boxes.

Instead use:

* thin directional paths
* compact nodes
* subtle animated flow
* queue depth markers
* small job tokens
* worker activity indicators
* heartbeat pulses
* throughput movement
* restrained status colors
* connected process diagrams

It should feel closer to an industrial systems visualization than an illustration.

---

# Design principles

## 1. Dense but calm

This is an engineering tool.

The interface should support high information density without feeling cluttered.

Prefer:

* compact rows
* small controls
* restrained typography
* short vertical rhythm
* precise alignment
* strong hierarchy

Avoid giant cards and oversized whitespace.

A developer should be able to see a meaningful amount of information on a 14-inch laptop.

---

## 2. Small components

Components should feel closer to Figma, Linear, Rider, GitHub, or professional developer tooling than consumer SaaS.

Buttons should generally be compact.

For example:

Small button:

* approximately 28-30px height

Normal button:

* approximately 32-34px height

Inputs:

* approximately 32-36px height

Table/list rows:

* approximately 36-44px depending on content

Badges:

* approximately 20-24px height

Sidebar items:

* compact
* no giant navigation blocks

Use a consistent spacing system.

Prefer a 4px base grid:

4
8
12
16
20
24
32
40
48

Avoid arbitrary margins such as 13px, 19px, 27px unless there is a strong optical reason.

Use mathematically consistent paddings, gaps, radii, icon sizes, and text alignment.

---

## 3. Minimal borders

Do not put every element inside a rounded card.

Use:

* whitespace
* subtle surface differences
* thin separators
* grouping
* typography

Reserve containers for actual conceptual groups.

Avoid "dashboard card soup."

---

## 4. Restricted border radius

Do not make everything pill-shaped.

Use something similar to:

* 4-6px for small controls
* 6-8px for panels
* full pill radius only for badges/tags where appropriate

---

## 5. Typography

Use a neutral, technical sans-serif style.

Think:

* Inter
* Geist
* system UI
* IBM Plex Sans

Monospace should be used selectively for:

* job IDs
* timestamps where useful
* queue names
* method names
* exception snippets
* server identifiers

Do not make the entire dashboard monospace.

Hierarchy should come mostly from weight, spacing, and contrast rather than huge font size differences.

---

# Color system

Support both dark and light themes, but prioritize a beautiful dark theme.

Do not use pure black.

The dark theme should use layered graphite / charcoal surfaces.

Example conceptual hierarchy:

background
surface-1
surface-2
surface-hover
border-subtle
border-strong

Statuses should have semantic colors.

For example:

Running:
blue/cyan

Queued:
neutral or cool gray

Scheduled:
purple

Succeeded:
green

Failed:
red

Retrying:
amber/orange

Cancelled:
muted gray

Do not flood entire cards with these colors.

Use color primarily through:

* small dots
* left-edge indicators
* progress paths
* badges
* icons
* timeline segments
* sparklines

The result should remain calm.

---

# Interaction design

The dashboard should feel alive.

Use subtle animation where it communicates actual system state.

Examples:

A running job can have a subtle moving indicator.

Worker heartbeat can gently pulse.

A queue lane can show small job markers entering processing.

Throughput graphs can animate when data changes.

A retry can briefly show the job travelling back toward the queue.

New log entries can appear smoothly.

Do NOT add decorative animation simply because it looks cool.

Animations should be:

* subtle
* fast
* purposeful
* interruptible
* reduced or disabled when prefers-reduced-motion is enabled

---

# Application architecture

This is an ASP.NET Core MVC application.

Build the UI as reusable components and partials.

Do not produce giant Razor files.

Think in terms of a proper design system.

Create reusable components for things such as:

* Button
* IconButton
* Badge
* StatusBadge
* Input
* SearchInput
* Select
* Dropdown
* Tabs
* SegmentedControl
* Tooltip
* Modal
* Drawer
* EmptyState
* Stat
* MiniChart
* DataTable
* Pagination
* Breadcrumb
* PageHeader
* FilterBar
* JobRow
* QueueRow
* WorkerRow
* Timeline
* LogViewer
* ExceptionPanel
* JobFlow
* QueueFlow
* WorkerNode
* MetricTile
* HealthIndicator
* ConfirmationDialog

Use:

* Razor partials
* ViewComponents where behavior/data warrants it
* tag helpers where appropriate
* shared layouts
* strongly typed view models

Avoid unnecessary frontend framework complexity.

Vanilla JavaScript or a lightweight interaction layer is preferred unless there is a strong reason otherwise.

---

# Global application layout

Create a compact application shell.

Desktop:

Left sidebar:

* product mark
* Overview
* Jobs
* Queues
* Recurring
* Scheduled
* Workers
* Servers
* Failed
* Metrics
* Settings

Bottom area:

* current environment
* authenticated user
* theme control

Main area:

Top contextual bar:

* breadcrumbs
* environment
* global search
* optional command palette trigger
* health indicator

Main content:

* constrained but wide enough for operational dashboards

Sidebar should be approximately 220-240px, not oversized.

Consider a collapsed sidebar mode.

---

# Overview page

This should be the signature page of the product.

Do not simply create six KPI cards followed by charts.

The central object should be a live visualization of the job processing system.

Create something like a "Processing Floor."

Example structure:

Scheduled
↓
Queues
↓
Workers
↓
Completed

with retry/failure paths branching from workers.

Each queue should show:

* queue name
* queued count
* throughput
* oldest job age
* active workers

Each worker group should show:

* active jobs
* capacity
* heartbeat
* average duration

Individual jobs can appear as very small tokens moving through the pipeline.

Do not animate hundreds of DOM nodes.

Represent activity intelligently.

For example:

3-8 representative tokens whose velocity/density reflects real throughput.

Below or beside this visualization include compact operational metrics:

* Jobs processed/min
* Success rate
* Failure rate
* Retry rate
* Queue latency
* Active workers

Add small sparklines rather than giant charts.

There should also be:

Recent failures
Slowest jobs
Queue pressure
Worker health

But these should visually support the main factory visualization rather than dominate it.

---

# Jobs page

Create a highly usable operational jobs explorer.

Top:

search
status filter
queue filter
date filter
worker filter
sort
refresh
saved filters if appropriate

List/table columns could include:

Status
Job
Queue
Created
Started
Duration
Attempts
Worker

The job name should be more visually important than the ID.

Example:

GenerateMonthlyInvoice

below it:

Billing.Jobs.GenerateMonthlyInvoiceJob

Use compact rows.

Clicking a job should open either:

* dedicated details page
  or
* large right-side inspector

Choose whichever produces the best operational UX.

---

# Job details page

This page is extremely important.

Show:

Job identity

Status
Job ID
Queue
Created
Scheduled
Started
Completed
Duration
Attempts
Worker/server

Then show a lifecycle timeline.

Example:

Created
↓
Scheduled
↓
Queued
↓
Claimed by worker-03
↓
Started
↓
Retry #1
↓
Queued
↓
Started
↓
Succeeded

Make the timeline compact but highly readable.

Also include:

Arguments / payload

Use an excellent JSON viewer.

Logs

Logs should look similar to serious developer tooling.

Columns:

timestamp
level
source
message

Allow:

filtering
search
level filtering
copy
expand multiline entries

Exceptions

Exception type
message
stack trace
inner exceptions

Make stack traces readable.

Use syntax-style presentation with clear file/method distinctions.

Execution metrics

Queue wait duration
Execution duration
Total elapsed
Retry delays

Actions

Retry
Delete
Cancel
Requeue

Destructive actions require confirmation.

---

# Queues page

Queues should be presented as active processing lanes rather than a boring table.

Each queue could have a compact horizontal representation:

critical     ● ● ● ● → [workers: 4/4] → 122/min
default      ● ●     → [workers: 7/8] → 341/min
emails       ● ● ● ● ● ● → [workers: 2/4] → 42/min

This should look polished and abstract, not ASCII.

Clicking a queue opens details:

Queued jobs
Active jobs
Workers
Throughput
Oldest waiting job
Failure rate
Latency history

---

# Workers page

Represent workers as a compact topology / station view.

Each worker node should display:

worker name
server
heartbeat
status
capacity
currently executing jobs
last activity
uptime

Workers should visually connect to the queues they consume from.

Provide both:

visual topology mode

and

table/list mode

Do not force the user to use the visualization for every task.

---

# Recurring jobs page

Show:

job
cron expression
human-readable schedule
queue
next execution
last execution
last result
enabled/disabled

Make cron expressions visually secondary to the human-readable meaning.

Example:

0 */15 * * * *

Every 15 minutes

Support a compact schedule preview.

---

# Failed jobs

Treat this as an incident/debugging workspace.

Important columns:

job
exception
queue
failed at
attempts
duration
worker

Add grouping where useful:

by exception type
by job type
by queue

Show patterns such as:

SqlException
42 failures

TimeoutException
17 failures

This is more valuable than forcing engineers to inspect failures one-by-one.

---

# Log viewer

Build an excellent log viewer.

Requirements:

compact lines

log level marker

timestamp

source/context

message

expandable structured metadata

search

filter levels

auto-scroll option

copy line

copy selected range

wrap/no-wrap toggle

Monospace should be used here.

Do not make it visually noisy.

---

# Metrics

Use small, precise operational visualizations.

Avoid giant marketing-style charts.

Prefer:

sparklines
compact line charts
histograms
distribution bars
percentiles

Useful metrics:

queue depth
processing rate
failure rate
retry rate
queue wait duration
execution duration
p50
p95
p99
worker utilization

Charts should prioritize readability over decoration.

---

# Login page

Create a polished login page that belongs to the same product.

Do not create the stereotypical:

left half gradient illustration
right half login form

Instead create something distinctive.

Possible concept:

A minimal authentication panel sits in front of a faint live-processing schematic.

The background may contain an extremely subtle representation of idle factory/job-processing lines.

Inputs:

Email
Password
Remember me
Forgot password
Sign in

Optional:

SSO / external provider authentication placeholder

The form should be compact and centered.

---

# Registration page

Match the login system.

Fields:

Name
Email
Password
Confirm password

Keep the form compact.

Show password requirements intelligently without huge text blocks.

---

# Empty states

Empty states should fit the factory metaphor.

Examples:

No queued jobs:
"The line is clear."

No failures:
"No failed jobs in this window."

No workers:
"No workers are connected."

Avoid cheesy illustrations.

Use simple geometric diagrams or icons.

---

# Microcopy

Use precise engineering language.

Avoid marketing language.

Good:

12 jobs waiting

Worker heartbeat missed

Retrying in 18s

Queue latency increasing

No jobs matched this filter

Bad:

Everything looks amazing!

Your jobs are working their magic!

Sit back while we process things!

---

# Responsiveness

The primary target is desktop developer usage.

Support:

1440px
1280px
MacBook-sized displays

The interface should remain usable at approximately 1024px width.

Mobile support is secondary but pages should not completely break.

Tables may collapse into structured rows where appropriate.

---

# Accessibility

Maintain:

strong contrast
visible keyboard focus
semantic HTML
keyboard-accessible menus
accessible form labels
ARIA where required
reduced-motion support

Status must never be communicated using color alone.

---

# Design tokens

Create a clear token system for:

spacing
font sizes
line heights
font weights
radii
colors
borders
shadows
component heights
z-index layers

Do not scatter magic numbers throughout CSS.

Example typography scale:

11px metadata
12px compact UI
13px default UI
14px important UI
16px section title
20-24px page title

Do not use 32-48px headings throughout the dashboard.

This is a tool, not a landing page.

---

# Shadows

Use shadows very sparingly.

The interface should mostly rely on:

surface hierarchy
borders
contrast

Use stronger shadows only for floating elements:

menus
dialogs
drawers
popovers

---

# Icons

Use one consistent icon family.

Examples:

Lucide
Heroicons

Do not mix icon styles.

Icons should generally be 14-18px.

---

# Important anti-AI-design constraints

Actively avoid patterns commonly produced by generic AI UI generators.

Do NOT:

* make every section a rounded card
* use huge 16-24px corner radii
* use enormous page headings
* add meaningless gradients
* add glowing cyan borders
* add excessive blur/glassmorphism
* fill space with fake analytics
* create oversized metric tiles
* use generic "Welcome back, John" dashboard headers
* use excessive pills
* put icons inside colored rounded squares everywhere
* create a hero section inside the application
* overuse purple gradients
* center everything
* create arbitrary floating shapes
* create fake decorative noise
* use lorem ipsum
* rely on random animation
* imitate a Dribbble concept that would be annoying in a real application

Every design decision should be justifiable from an engineering workflow.

---

# Product identity

Give the product a distinct visual identity.

The identity should come from:

the processing-floor visualization
queue lanes
worker nodes
job movement
execution timelines
precise technical typography
compact spatial rhythm

Not from logos, gradients, or illustration.

Someone seeing a screenshot should eventually recognize:

"That is the job processing dashboard."

---

# Implementation expectations

Do not only create static mockups.

Implement the application structure.

Create:

shared layout

navigation

design tokens

core component library

representative pages

realistic mock data

interactive states

hover states

focus states

loading states

empty states

error states

dropdowns

modals

drawers

filters

job inspector

basic live-update simulation where useful

Use clean Razor views.

Avoid duplicated markup.

Create reusable partials / ViewComponents.

Keep CSS maintainable and organized.

Prefer native CSS capabilities where practical.

Do not introduce a massive UI dependency unless it clearly improves the application.

---

# Initial pages to implement

Implement at minimum:

1. Login
2. Registration
3. Overview / Processing Floor
4. Jobs
5. Job Details
6. Queues
7. Queue Details
8. Recurring Jobs
9. Scheduled Jobs
10. Workers
11. Failed Jobs
12. Metrics
13. Settings

---

# Sample domain data

Use believable .NET job names such as:

SendOrderConfirmationJob
GenerateMerchantSettlementJob
ProcessRefundJob
RecalculateMerchantRatingJob
SyncPartnerCatalogJob
ExpireReservationsJob
SendNotificationJob
RefreshMerchantSearchIndexJob

Queues:

critical
default
payments
notifications
catalog
maintenance

Workers:

worker-food-01
worker-food-02
worker-payments-01
worker-notifications-01

Use realistic durations, timestamps, retry counts, errors, and throughput numbers.

Avoid fake nonsense data.

---

# Development process

Before implementing the full interface:

1. Inspect the existing ASP.NET MVC project structure.

2. Define the design system:

   * tokens
   * typography
   * spacing
   * colors
   * component sizing
   * interaction rules

3. Define the reusable Razor component structure.

4. Implement the application shell.

5. Build the Processing Floor / Overview first.

This page establishes the visual language for the entire product.

6. Build jobs and job details.

These pages establish the operational information density.

7. Build queues and workers using the same visual language.

8. Build authentication pages.

9. Refine consistency across all components.

Do not blindly generate all pages independently.

The application must feel like one coherent design system.

---

# Final quality bar

Treat this as a product that could compete with mature developer infrastructure tools.

Every element should answer:

Why is this here?
Why is it this size?
Why is it grouped this way?
Why is it interactive?
What operational question does it help answer?

If a visual element exists only to make the screenshot look more impressive, remove it.

The goal is a dashboard that is:

modern
technical
compact
interactive
distinctive
high-information-density
beautiful
usable for hours at a time

The strongest idea should be the live factory/process visualization, but it must stay restrained enough to work as serious production software.

Do not produce a generic dashboard and then decorate it with factory icons.

Design the information architecture itself around how jobs flow through the system.
