---
name: principle-clean-architecture-dotnet
description: The layered architecture for a .NET system of this class — Domain / Application / Infrastructure with thin composition-root hosts (API, Worker, UI). Defines dependency direction, what each layer may reference, the patterns this standard deliberately omits, and how SOLID is applied. Use when placing a new type, adding a project reference, or deciding where a concern belongs.
---

# Clean Architecture (.NET)

## Principle (why)

The architecture exists to make the dependency direction a compile-time fact, not a discipline. Business rules (Domain, Application) must not depend on delivery mechanisms (HTTP, the database, the message broker, the UI) — so those concrete concerns live in outer layers that depend *inward*, never the reverse. When the direction is enforced by project references, an accidental coupling fails the build instead of rotting silently. The reward is that the core is testable without infrastructure, and infrastructure is swappable without touching the core.

A second, quieter principle: **prefer the simplest construct that holds the invariant.** This standard deliberately omits several "enterprise" patterns (below) because, for a system of this size and shape, they add ceremony without buying a real invariant. Omitting them is a decision, not an oversight — reintroducing one needs a reason tied to a concrete problem.

## Pattern (what)

**Layers and allowed references** (each references only inward):

| Layer | Holds | May reference |
|---|---|---|
| **Domain** | Entities, enums, domain extension methods. No base classes, no value objects, no domain events. | Nothing |
| **Application** | Service + repository interfaces, DTOs, the result type, configuration, message contracts | Domain |
| **Infrastructure** | Repository + service implementations, external adapters, resilience policies | Application + Domain |
| **API host** | Composition root — DI wiring, endpoint mapping, auth, hosts the interactive UI; may host UI-adjacent background work | All |
| **Worker host** | Composition root — message handlers and scheduled background jobs | All |
| **UI** | Presentation (e.g. a Razor Class Library) | Application only — never Infrastructure or the API host |

The hosts (API, Worker) are thin composition roots: they wire and host, they don't hold business logic. The UI depends on the Application *contracts* only — it reaches the backend through those interfaces (or a facade over them), never through Infrastructure.

**Patterns this standard deliberately omits** (use the simpler alternative):
- Domain events → use explicit message/command contracts dispatched by the broker.
- Value objects → primitives or simple classes.
- Specification pattern → LINQ in repositories.
- Rich domain models → entities are mostly data; reserve state-transition methods for the few entities with a genuine lifecycle.
- Aggregate roots → entities are standalone.
- Event sourcing → standard ORM persistence.
- A separate validation framework → the platform's built-in DataAnnotations.
- Third-party UI state containers → scoped services + the framework's own state primitives.
- A second identity stack → one external OIDC provider.

(A curated OpenAPI surface is the one place generated docs are used — only for the public integration contract, not the whole API.)

## SOLID, applied to this shape

- **Single Responsibility** — split large service domains by concern (commands / queries / specialized I/O) rather than one god-service per entity.
- **Dependency Inversion** — interfaces in Application, implementations in Infrastructure; the core depends on the abstraction.
- **Interface Segregation** — separate query and command service interfaces so a consumer depends only on what it uses.

## Boundaries

- The omitted-patterns list is this standard's default, not a universal law. If a feature presents a real invariant that a value object or a domain event genuinely protects, that's a design conversation — surface it, don't smuggle it in.
- "Thin host" is the test for the composition roots: if business logic is accreting in an endpoint or a `Program.cs`, it belongs in a service.
