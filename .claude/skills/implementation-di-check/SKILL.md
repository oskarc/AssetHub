---
name: implementation-di-check
description: Cross-check AssetHub's Application interfaces against their DI registrations in ServiceCollectionExtensions and InfrastructureServiceExtensions. Use after adding services, repositories, Wolverine handlers, or BackgroundServices, or when a runtime "Unable to resolve service" error is suspected.
---

# AssetHub DI Registration Check

Every `I*Service` / `I*Repository` / `BackgroundService` / Wolverine handler needs a registration. Forget one and the failure is runtime-only — sometimes request-scoped, sometimes worker-scoped, always annoying. This skill diffs intent (interfaces and background types) against reality (registration code) and reports the gap.

## How to run

1. **Gather the target sets**:
   - Application service interfaces: `src/AssetHub.Application/Services/I*.cs` — one file per interface.
   - Application repository interfaces: `src/AssetHub.Application/Repositories/I*.cs`.
   - Concrete `BackgroundService` / `IHostedService` classes: grep `src/AssetHub.Api/` and `src/AssetHub.Worker/` for `: BackgroundService` and `: IHostedService`.
   - Concrete Wolverine handlers: files under `src/AssetHub.Worker/Handlers/` ending in `Handler.cs` with a `HandleAsync(...)` method.
2. **Gather the registration sets**:
   - `src/AssetHub.Api/Extensions/ServiceCollectionExtensions.cs` — app services, scoped registrations, hosted services.
   - `src/AssetHub.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` — repos, infra adapters, resilience pipelines.
   - `src/AssetHub.Api/Program.cs` and `src/AssetHub.Worker/Program.cs` — Wolverine queue routes, `AddHostedService<T>`, any inline registrations.
3. **Diff** the sets using the rules below.
4. **Verify** implementation pairing: each interface should have exactly one implementation class, named the same minus the leading `I`, in the expected project layer.
5. **Report** findings grouped by category with file:line for registrations and a one-line explanation per gap.
6. **Offer to apply** fixes — add the missing registration in the right file, in the right section. Ask before applying.

## Rules

### Services

- Every `IFooService` in `AssetHub.Application/Services/` must have **exactly one** `services.AddScoped<IFooService, FooService>()` in `ServiceCollectionExtensions.cs`.
- Exception: Wolverine-handled services register the concrete class first (`AddScoped<FooService>()`), then forward the interface (`AddScoped<IFooService>(sp => sp.GetRequiredService<FooService>())`). Both lines must exist.
- Command/query split services (e.g., `IMetadataSchemaService` + `IMetadataSchemaQueryService`) must both be registered separately.

### Repositories

- Every `IFooRepository` in `AssetHub.Application/Repositories/` must have **exactly one** `services.AddScoped<IFooRepository, FooRepository>()` in `InfrastructureServiceExtensions.cs` under the "Repositories" section.
- Concrete must live in `AssetHub.Infrastructure/Repositories/FooRepository.cs`.

### Background services

- Every concrete `BackgroundService` / `IHostedService` must be registered via `AddHostedService<T>()` in either `ServiceCollectionExtensions.cs` (API-side) or `Program.cs` (Worker-side).
- Flag any class that extends `BackgroundService` but has no `AddHostedService` line.

### Wolverine handlers

- Every handler in `src/AssetHub.Worker/Handlers/*Handler.cs` must correspond to a message defined in `AssetHub.Application/Messages/`.
- The queue it listens on must be routed in `Program.cs` — either `opts.PublishMessage<TCommand>().ToRabbitQueue("queue-name")` on the publisher side or `opts.ListenToRabbitQueue("queue-name")` on the worker side.
- Flag handlers without matching publish/listen configuration.

### Configuration / options

- Every `*Settings` class in `AssetHub.Application/Configuration/` with a `const string SectionName` must have a matching `services.AddOptions<T>().Bind(config.GetSection(T.SectionName))`.
- Critical infra settings (Keycloak, MinIO, Postgres, RabbitMQ) must additionally call `.ValidateOnStart()` per CLAUDE.md.

### Naming integrity

- Interface `IFoo` → concrete `Foo` (strip the `I`). Flag mismatches (`IFoo` → `FooService`, `IBarStore` → `BarRepository`) — either the interface or the class is misnamed.
- Concrete sealed-ness: per CLAUDE.md services and repos are `sealed`. Flag non-sealed concretes.

### No duplicates

- Flag the same interface registered twice (`AddScoped<IFoo, A>` and `AddScoped<IFoo, B>` — last-wins will silently drop the first).
- Flag `AddSingleton` where `AddScoped` is used by neighbors (and vice versa) — consistency per layer.

## Output

```
Missing registrations
  src/AssetHub.Application/Services/IBazService.cs
    → add `services.AddScoped<IBazService, BazService>();`
       in ServiceCollectionExtensions.cs under the matching section

Mismatched names
  IReportService → ReportingService (expected ReportService)

Orphaned implementations
  src/AssetHub.Infrastructure/Services/OldThing.cs — no interface, no registration, 0 references

Hosted services
  SearchReindexWorker : BackgroundService — not registered via AddHostedService
```

Finish with a total count per category. If everything is wired, say so plainly.

## Abort conditions

- A type marked `[Obsolete]` or clearly abandoned — report but don't suggest re-registering it.
- DI changes that would require cross-project coordination (e.g., moving a service between layers) — report only; don't rewrite the project structure.
