# Project Agent Instructions

## Convention Decisions

When a coding convention or implementation approach is explicitly agreed upon, record it in this section before completing the work. Update an existing entry when it supersedes or refines a prior decision; do not add duplicate or conflicting rules.

Each entry must state the convention and, when useful, its scope or rationale.

### Established Conventions

| Area | Convention |
| --- | --- |
| Compiler strictness | Treat all compiler warnings as errors in every project. |
| Nullable references | Enable nullable reference types and resolve all nullability warnings. |
| Naming | Follow Microsoft C# naming: PascalCase types and members, camelCase parameters and locals, `_camelCase` private fields, `I`-prefixed interfaces, and an `Async` suffix for asynchronous methods. |
| Formatting | Define C# formatting in the repository-root `.editorconfig` and enforce formatting and analyzer violations in CI. |
| Namespaces | Use file-scoped namespaces for new C# files unless a nested namespace is required. |
| Local variables | Use `var` when the assigned expression makes the type obvious; otherwise use the explicit type. |
| Usings | Use global usings for common framework namespaces and file-level usings for all other namespaces. |
| Asynchrony | Use asynchronous APIs end-to-end for naturally asynchronous work. Do not block on tasks; name asynchronous methods with the `Async` suffix. |
| Cancellation | Accept and propagate `CancellationToken` through cancellable operations. Use `CancellationToken.None` only for intentionally non-cancellable work. |
| Disposal | Use `await using` for `IAsyncDisposable` resources and `using` for synchronous resources. |
| Interfaces | Introduce interfaces at external boundaries, for public contracts, or when a concrete dependency needs substitution. Do not create an interface for every service. |
| Types | Use immutable records for value/data models. Use classes for entities, services, and mutable identity-bearing types. |
| Dependency injection | Use constructor injection. Do not use service locators or static dependency access. |
| Validation | Validate untrusted input at API, messaging, CLI, and persistence boundaries. Domain methods may assume validated input. |
| Expected failures | Represent expected business or validation failures with result/error values. Reserve exceptions for exceptional or infrastructure failures. |
| Testing | Add or update focused automated tests for every behavior change and bug fix. Use xUnit and FluentAssertions. |
| Documentation | Require XML documentation comments on all public types and members. |
| Project overview | Keep `PROJECT_OVERVIEW.md` current when project goals, architecture, MVP scope, or component responsibilities change. |
| Logging | Use injected `ILogger` with structured message templates. Never log sensitive values. |
| Packages | Add package dependencies only when they are maintained and justified; prefer BCL or framework capabilities where suitable. |
