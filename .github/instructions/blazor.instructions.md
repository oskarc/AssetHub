---
description: 'Blazor component and application patterns'
applyTo: '**/*.razor, **/*.razor.cs, **/*.razor.css'
---

# Blazor — AssetHub Overrides

These rules override generic Blazor guidance for the AssetHub project. See also `blazor-ui.instructions.md` for detailed UI conventions.

## Project-Specific Rules

- **C# 13** (.NET 9) — do not use C# 14 features.
- **MudBlazor 8** is the component library — use MudBlazor components, not raw HTML or other libraries.
- **DataAnnotations only** — do not use FluentValidation. DTOs use `[Required]`, `[StringLength]`, `[Range]`.
- **`AssetHubApiClient`** for all HTTP calls — never use `HttpClient` directly.
- **No third-party state management** — no Fluxor, BlazorState, or Blazored.LocalStorage. Use scoped services, `CascadingAuthenticationState`, and `MudDialogService`.
- **bUnit + xUnit + Moq** for component testing — not Visual Studio Enterprise specific tooling.
- **`IStringLocalizer<T>`** for all user-facing strings — Swedish (sv) and English (default).
- **`ErrorBoundary`** for UI error handling.
- **Keycloak OIDC** for auth — not ASP.NET Identity.
- **No Swagger/OpenAPI** — endpoints are consumed by the Blazor UI, not external API clients.
- **HybridCache** (L1 in-memory + L2 Redis) — not `IMemoryCache` or localStorage/sessionStorage.

## General Blazor Patterns Still Apply

The generic Blazor instruction guidance for the following topics remains valid:
- Component lifecycle (`OnInitializedAsync`, `OnParametersSetAsync`).
- Data binding with `@bind`.
- Dependency injection in components.
- Separation of concerns between components and services.
- Async/await for non-blocking UI.
- `ShouldRender()` and `StateHasChanged()` optimization.
- `EventCallback` for component communication.
- PascalCase for types/methods, camelCase for locals/fields.
