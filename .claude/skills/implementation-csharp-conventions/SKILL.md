---
name: implementation-csharp-conventions
description: Mechanical C# conventions for a modern nullable-enabled codebase — null-checking style, sealing, readonly/static defaults, banned constructs (empty catch, nested ternary, FP equality, hardcoded credential defaults), and the idiomatic surface (primary ctors, file-scoped namespaces, structured logging). Use while writing or reviewing any C# file. Many map to specific Sonar rules.
---

# C# Conventions

These are mechanical, deterministic rules — apply them while writing, not as a later cleanup pass. Most carry a Sonar rule id so the analyser catches a miss, but writing it right first is cheaper than the round-trip.

## Null checking
- Nullable reference types enabled globally. Use `is null` / `is not null` in plain C# — never `== null` / `!= null`.
- **The lone exception:** inside an ORM query expression tree that translates to SQL (`.Where(...)`, `.Count(...)`, projections), `== null` / `!= null` is load-bearing — the translator understands it and may not understand `is null`. Use `==`/`!=` only there.

## Sealing and field/method modifiers
- `sealed` on every service, repository, adapter, and background-service implementation — no exceptions. A concrete class not designed for inheritance says so. If you're writing `public class Foo : IFoo`, make it `public sealed class` in the same keystroke. Same for private nested classes/records (`private sealed class FormModel`, `private sealed record ...`). (Sonar S3260.)
- `private readonly` for fields assigned only at declaration and mutated only through methods — `CancellationTokenSource`, backing collections, ref holders. (S2933.) The exception is genuine reassignment patterns; keep those mutable.
- `private static` for methods that don't touch instance state — pure helpers, validators, mappers. (S2325.)

## Banned constructs
- **No empty catch blocks.** `catch (X) { }` is S108 and a smell. Either fill it with a one-line comment stating why the exception is benign (`/* circuit gone — module unreachable */`), or delete the catch and let it bubble. Empty-with-a-reason is fine; empty-with-nothing is not.
- **No nested ternaries** (`a ? b : c ? d : e`, S3358). Hoist branches into locals or an `if/else if` chain. Object-initializer bodies are the common offender — extract the branch above the `new { ... }`.
- **No floating-point `==`** (S1244). Use `Math.Abs(a - b) < epsilon` or a shared `IsApprox` helper.
- **No hardcoded credential defaults, including "well-known" ones** (S2068). A `?? "guest"` / `?? "admin"` / `?? "postgres"` fallback for a username/password is a vulnerability — use `?? string.Empty` and let startup validation fail loudly on missing config. UI mask placeholders (`"********"`) aren't credentials but need an explicit `[SuppressMessage(... "S2068" ...)]` with justification to quiet the rule.

## Idiomatic surface
- Primary constructors for DI injection in services and repositories.
- DataAnnotations only for DTO validation (`[Required]`, `[StringLength]`, `[Range]`).
- File-scoped namespaces; pattern matching; `nameof`.
- `async`/`await` for I/O-bound work.
- Structured logging with named placeholders: `logger.LogInformation("Processed {Id}", id)` — never string interpolation into the message template.
- PascalCase for types/methods/public members, camelCase for locals/fields, `I` prefix for interfaces.
- Apply the repository-root `.editorconfig`.

## Why these and not a longer list
Each rule here either prevents a security/correctness bug (credential defaults, empty catch, FP equality) or removes a recurring review nit that an analyser flags anyway (sealing, readonly, static, nested ternary). They're worth stating because they're cheap to get right up front and expensive to retrofit across a large diff. Style choices the `.editorconfig` already enforces don't need restating here.
