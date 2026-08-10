---
name: implementation-di-check
description: Cross-check the project's Application interfaces against their DI registrations in ServiceCollectionExtensions and InfrastructureServiceExtensions. Use after adding services, repositories, message-bus handlers, or BackgroundServices, or when a runtime "Unable to resolve service" error is suspected.
---

# DI Registration Check

Every `I*Service` / `I*Repository` / `BackgroundService` / message-bus handler (e.g., Wolverine) needs a registration. Forget one and the failure is runtime-only — sometimes request-scoped, sometimes worker-scoped, always annoying. This skill diffs intent (interfaces and background types) against reality (registration code) and reports the gap.

## How to run

1. **Gather the target sets**:
   - Application service interfaces: `src/<App>.Application/Services/I*.cs` — one file per interface.
   - Application repository interfaces: `src/<App>.Application/Repositories/I*.cs`.
   - Concrete `BackgroundService` / `IHostedService` classes: grep `src/<App>.Api/` and `src/<App>.Worker/` for `: BackgroundService` and `: IHostedService`.
   - Concrete message-bus handlers: files under `src/<App>.Worker/Handlers/` ending in `Handler.cs` with a `HandleAsync(...)` method.
2. **Gather the registration sets**:
   - `src/<App>.Api/Extensions/ServiceCollectionExtensions.cs` — app services, scoped registrations, hosted services.
   - `src/<App>.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` — repos, infra adapters, resilience pipelines.
   - `src/<App>.Api/Program.cs` and `src/<App>.Worker/Program.cs` — message-bus queue routes, `AddHostedService<T>`, any inline registrations.
3. **Diff** the sets using the rules below.
4. **Verify** implementation pairing: each interface should have exactly one implementation class, named the same minus the leading `I`, in the expected project layer.
5. **Report** findings grouped by category with file:line for registrations and a one-line explanation per gap.
6. **Offer to apply** fixes — add the missing registration in the right file, in the right section. Ask before applying.

## Rules

### Services

- Every `IFooService` in `<App>.Application/Services/` must have **exactly one** `services.AddScoped<IFooService, FooService>()` in `ServiceCollectionExtensions.cs`.
- Exception: bus-handled services register the concrete class first (`AddScoped<FooService>()`), then forward the interface (`AddScoped<IFooService>(sp => sp.GetRequiredService<FooService>())`). Both lines must exist.
- Command/query split services (e.g., `IFooService` + `IFooQueryService`) must both be registered separately.

### Repositories

- Every `IFooRepository` in `<App>.Application/Repositories/` must have **exactly one** `services.AddScoped<IFooRepository, FooRepository>()` in `InfrastructureServiceExtensions.cs` under the "Repositories" section.
- Concrete must live in `<App>.Infrastructure/Repositories/FooRepository.cs`.

### Background services

- Every concrete `BackgroundService` / `IHostedService` must be registered via `AddHostedService<T>()` in either `ServiceCollectionExtensions.cs` (API-side) or `Program.cs` (Worker-side).
- Flag any class that extends `BackgroundService` but has no `AddHostedService` line.

### Message-bus handlers

- Every handler in `src/<App>.Worker/Handlers/*Handler.cs` must correspond to a message defined in `<App>.Application/Messages/`.
- The queue it listens on must be routed in `Program.cs` — publish-side and listen-side wiring both present (e.g., Wolverine's `opts.PublishMessage<TCommand>().ToRabbitQueue(...)` / `opts.ListenToRabbitQueue(...)`).
- Flag handlers without matching publish/listen configuration.

### Configuration / options

- Every `*Settings` class in `<App>.Application/Configuration/` with a `const string SectionName` must have a matching `services.AddOptions<T>().Bind(config.GetSection(T.SectionName))`.
- Critical infra settings (identity provider, object storage, database, message bus) must additionally call `.ValidateOnStart()` per the config-secrets standard (implementation-config-secrets).

### Naming integrity

- Interface `IFoo` → concrete `Foo` (strip the `I`). Flag mismatches (`IFoo` → `FooService`, `IBarStore` → `BarRepository`) — either the interface or the class is misnamed.
- Concrete sealed-ness: per the C# conventions standard (implementation-csharp-conventions) services and repos are `sealed`. Flag non-sealed concretes.

### No duplicates

- Flag the same interface registered twice (`AddScoped<IFoo, A>` and `AddScoped<IFoo, B>` — last-wins will silently drop the first).
- Flag `AddSingleton` where `AddScoped` is used by neighbors (and vice versa) — consistency per layer.

## Output

```
Missing registrations
  src/<App>.Application/Services/IBazService.cs
    → add `services.AddScoped<IBazService, BazService>();`
       in ServiceCollectionExtensions.cs under the matching section

Mismatched names
  IReportService → ReportingService (expected ReportService)

Orphaned implementations
  src/<App>.Infrastructure/Services/OldThing.cs — no interface, no registration, 0 references

Hosted services
  SearchReindexWorker : BackgroundService — not registered via AddHostedService
```

Finish with a total count per category. If everything is wired, say so plainly.

## Abort conditions

- A type marked `[Obsolete]` or clearly abandoned — report but don't suggest re-registering it.
- DI changes that would require cross-project coordination (e.g., moving a service between layers) — report only; don't rewrite the project structure.
