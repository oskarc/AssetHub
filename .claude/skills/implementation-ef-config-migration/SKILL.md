---
name: implementation-ef-config-migration
description: ORM model-configuration and migration safety for a codebase that auto-migrates on startup — per-entity config classes, byte-identical-model discipline, reversible/idempotent migrations, and the build-time guard that refuses to start on model drift. Use when changing an entity's mapping, adding a migration, or touching the DbContext. Pairs with implementation-migration-check (the post-build audit).
---

# Entity configuration & migration safety

## Principle (why)

When the application **auto-applies migrations on startup**, a migration is production code that runs before the app serves a request — a bad one takes the system down or, worse, silently ships a schema that disagrees with the code. Two guards make this safe: the model must never drift from the migration history undetected, and every migration must be reversible and re-runnable. The configuration discipline (one config class per entity) exists for navigability and to keep the model declarative, but it serves the same end — a model you can read entity-by-entity is a model whose drift you can reason about.

## Pattern (what)

**Per-entity configuration.** Each entity's mapping lives in its own `IEntityTypeConfiguration<T>` class (one file per entity), applied from the model-building hook. Shared conventions (JSON column type, value comparers/converters) live in one shared helper the config classes reference — not duplicated, not private to the context. Never add a new inline mapping block to the model-building method; add a config class.

**Byte-identical model discipline.** Refactoring *how* the model is configured (e.g. splitting inline blocks into classes) must not change *what* the model is. Verify it: the tooling's "has-pending-model-changes" check must report none, and the build-time drift guard (below) must stay green. A configuration change that alters the model ships *with* its migration or not at all.

**Safe migrations.**
- Every `Up` has a `Down` that reverses it. Never combine, in one migration: a drop + removing the code that references it; a rename without a data move; a type change without conversion SQL.
- **A hand-written migration must be discovery-verified.** The framework finds migrations via the `[Migration("id")]` attribute, which the generated Designer file normally carries. A hand-authored migration without it compiles, reads plausibly, and is silently *never applied* — environments provisioned by the migration chain simply lack its schema, while model-derived provisioning (test fixtures using EnsureCreated) has it, hiding the gap from every test. After hand-writing a migration: carry the attribute (and a Designer or its equivalent), then confirm the migration appears in `dotnet ef migrations list`. A migration that isn't listed does not exist.
- **Verification tooling that reads build output is only as current as the last build.** A checker pointed at stale binaries reports on the code you *had*, and reports it with the same confidence as the truth — so the failure mode is a confident wrong answer, not an error. Never pass a skip-build flag to a check you intend to trust. Concretely: `dotnet ef migrations list --no-build` omits a migration scaffolded since the last compile, and `dotnet ef migrations has-pending-model-changes --no-build` reports pending changes that a rebuild shows are already captured. Both invert after a fresh build. The rule generalises past EF — it holds for any analyser, linter, or model checker that loads compiled assemblies.
- Raw SQL in migrations is **idempotent** (`IF NOT EXISTS` etc.) — both because auto-migration may race across instances (first to acquire the lock applies) and because re-runs must be safe.
- Index naming is conventional and predictable (`idx_{entity}_{fields}`, `_unique` suffix for unique).
- Foreign keys specify delete behavior explicitly.

**The build-time drift guard.** The framework's "pending-model-changes" warning is configured to **throw outside development** (and log inside it). So CI/staging/production refuse to start when the model has drifted from the latest migration — a forgotten "add migration" fails fast instead of silently shipping a mismatched schema. Don't downgrade this guard to quiet a startup error; generate the missing migration instead.

## Implementation constraints (how)

- JSON/document columns: declare the column type, a serialization converter, **and** a value comparer — the comparer is what makes change-tracking correct; omitting it causes spurious or missed updates.
- Special index types the ORM can't express (full-text, trigram) go in via raw idempotent SQL in the migration.

## Boundaries

- This is the *during-build standard*. The post-build audit of a specific migration (does the Down really reverse the Up, is there a destructive op) is `implementation-migration-check` — run it on new migrations.
- "Byte-identical" applies to *refactors* of configuration. A deliberate schema change is expected to produce a migration — that's the normal path, not a violation.
