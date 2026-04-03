---
description: 'Guidelines for building C# applications'
applyTo: '**/*.cs'
---

# C# — AssetHub Overrides

These rules override generic C# guidance for the AssetHub project.

## Project-Specific Rules

- **C# 13** (.NET 9) — do not use C# 14 features.
- **DataAnnotations only** for validation — do not use FluentValidation. DTOs use `[Required]`, `[StringLength]`, `[Range]`.
- **`sealed`** on all service and repository implementations.
- **Primary constructors** preferred for DI injection in services and repositories.
- **Nullable reference types** enabled globally — use `is null` / `is not null`, never `== null`.
- **PostgreSQL + EF Core 9** — not SQL Server or SQLite.
- **Keycloak OIDC** for auth — not Microsoft Entra ID or ASP.NET Identity.
- **`ServiceResult<T>`** for business error handling — never throw for expected errors. No RFC 9457 Problem Details pattern.
- **No Swagger/OpenAPI** — endpoints are consumed by the Blazor UI, not external API clients.
- **Apply `.editorconfig`** formatting rules from the repository root.
- **xUnit + Moq + Testcontainers** for testing — not NUnit or MSTest.

## General C# Patterns Still Apply

The generic C# instruction guidance for the following topics remains valid:
- PascalCase for types/methods/public members, camelCase for locals/fields.
- `I` prefix for interfaces.
- File-scoped namespaces, pattern matching, `nameof`.
- Async/await for I/O-bound operations.
- Structured logging with named arguments.
- Test naming: `MethodName_Condition_ExpectedResult`.
- No `== null` — use `is null` / `is not null`.
