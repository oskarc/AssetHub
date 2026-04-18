---
applyTo: "src/AssetHub.*/**"
description: "Self-validation checks to run before reporting a task done. Prevents hallucinated types, broken builds, and missing resources."
---
# Self-Check — Verify Before Done

Run through the applicable checks below before reporting a task complete. These catch the most common agent mistakes.

## After generating or modifying C# code
1. **Build check** — run `dotnet build --no-restore` on the affected project(s). Fix any errors before proceeding.
2. **Type existence** — before referencing a class, interface, enum, or method, grep the workspace to confirm it exists. Never assume a type is available.
3. **Namespace imports** — verify `using` statements match the actual namespaces of referenced types.

## After modifying tests
1. **Run the affected tests** — `dotnet test --no-build --filter "FullyQualifiedName~ClassName"` on the test project.
2. **Confirm naming** — test methods follow `MethodName_Condition_ExpectedResult`.
3. **Fixture usage** — confirm the test class uses the correct fixture (`[Collection("Database")]` or `[Collection("Api")]`).

## After adding localization keys
1. **Both files updated** — confirm the key exists in both `*.resx` and `*.sv.resx` for the same resource domain.
2. **Key pattern** — verify the key follows `Area_Context_Element` naming.
3. **Marker class** — if a new resource domain, verify a marker class exists in `ResourceMarkers.cs`.

## After adding cache keys
1. **No naming conflicts** — grep `CacheKeys.cs` for the new prefix to ensure it doesn't collide with existing ones.
2. **Tag defined** — if using tag-based invalidation, confirm the tag exists in `CacheKeys.Tags`.
3. **Invalidation wired** — confirm the service that mutates the data calls `RemoveByTagAsync` or `RemoveAsync` after create/update/delete.

## After modifying endpoints
1. **Registration** — confirm the new endpoint class is called in `WebApplicationExtensions.MapAssetHubEndpoints()`.
2. **Auth policy** — confirm the route group has `.RequireAuthorization(...)`.
3. **Antiforgery** — confirm POST/PATCH/DELETE endpoints have `.DisableAntiforgery()`.

## After modifying DbContext or entities
1. **Migration needed** — if schema changed, a new migration must be generated (or defer to the `migration` agent).
2. **ValueComparer** — if a JSONB column was added, confirm the `OnModelCreating` config includes a `ValueComparer`.
3. **Enum storage** — if a new enum was added, confirm `ToDbString()` and reverse extension methods exist in `Enums.cs`.

## After modifying DI registration
1. **Service registered** — grep `InfrastructureServiceExtensions.cs` or `ServiceCollectionExtensions.cs` to confirm the new service/repo is registered.
2. **Interface forwarding** — if the service is consumed by Wolverine, confirm concrete-first + interface-forward registration.

## When in doubt
- Run `dotnet build --configuration Release` on the entire solution — it must pass with zero warnings.
- Check `get_errors` for any compile or lint issues in the files you touched.
