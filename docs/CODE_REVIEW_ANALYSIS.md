# AssetHub — Architecture & Code Review Analysis

## Architecture Overview

AssetHub is a Digital Asset Management (DAM) system built on .NET 9 with Blazor Server UI,
Keycloak OIDC auth, PostgreSQL, MinIO object storage, and Hangfire background processing.

### Layer Diagram

```
┌─────────────────────────────────────────────────────────────┐
│  Dam.Ui (Blazor Server)                                     │
│  - MudBlazor components, Pages, AssetHubApiClient           │
│  - Localized (EN/SV), CookieForwardingHandler for auth      │
├─────────────────────────────────────────────────────────────┤
│  AssetHub (Host / API)                                      │
│  - Minimal API endpoints (Asset, Collection, Share, Admin)  │
│  - Auth pipeline (OIDC + JWT + Cookie "Smart" scheme)       │
│  - Middleware, HealthChecks, Serilog                         │
├─────────────────────────────────────────────────────────────┤
│  Dam.Application (Core / Ports)                             │
│  - Service interfaces, Repository interfaces                │
│  - DTOs, ServiceResult pattern, CurrentUser                 │
│  - Configuration POCOs, Helpers, InputValidation            │
├─────────────────────────────────────────────────────────────┤
│  Dam.Domain (Entities)                                      │
│  - Asset, Collection, CollectionAcl, Share, AuditEvent      │
│  - Zero dependencies — persistence-ignorant                 │
├─────────────────────────────────────────────────────────────┤
│  Dam.Infrastructure (Adapters)                              │
│  - EF Core (Npgsql), Repository implementations            │
│  - Service implementations (MinIO, Keycloak, SMTP, Media)  │
│  - Hangfire job processing, audit persistence               │
├─────────────────────────────────────────────────────────────┤
│  Dam.Worker (Migration Runner)                              │
│  - Runs EF migrations in a separate container               │
└─────────────────────────────────────────────────────────────┘
```

### Dependency Flow (correct direction)
`UI → Host → Application ← Infrastructure ← Domain`

### Key Design Patterns
- **ServiceResult<T>** for orchestration-layer error flow (replaces exceptions)
- **Repository pattern** with async + CancellationToken throughout
- **Two-tier error model**: orchestration services return ServiceResult; low-level services throw
- **CurrentUser scoped service** decouples business logic from HttpContext (mostly)
- **Hierarchical RBAC**: Collection ACLs with parent-chain inheritance

## Cohesion Assessment

| Boundary                    | Cohesion | Notes                                            |
|-----------------------------|----------|--------------------------------------------------|
| Domain ↔ Application        | **Good** | Domain is clean, no infrastructure leaks         |
| Application ↔ Infrastructure| **Good** | Interfaces properly abstracted                   |
| Application ↔ HTTP          | **Weak** | HttpContext leaks into 4 service interfaces       |
| Host ↔ UI                   | **Good** | API client cleanly separates concerns            |
| Service ↔ Service           | **Mixed**| Some services return ServiceResult, others throw |
| DTO naming                  | **Weak** | Mixed *Dto/*Request/*Response, record vs class   |

## Top Issues by Severity

### Critical (data loss / security / runtime crash)

1. **Hangfire dashboard has no auth** — accessible to anyone in production
2. **Serilog.Formatting.Compact missing** from csproj — Production startup crash
3. **Missing ValueComparer on jsonb columns** — MetadataJson mutations silently not persisted
4. **Open redirect** on `/auth/login?returnUrl=` — no relative-URL validation
5. **AuditService.SaveChangesAsync** flushes sibling uncommitted EF changes

### High (bugs / incorrect behavior)

6. **Assets.razor filters don't reload data** — `@bind-Value` without `ValueChanged`
7. **AssetDetail.razor shows edit/delete/share to all roles** — no permission check
8. **ShareService.ValidateScopeAsync** checks only first collection — wrong auth result
9. **AssetService.GetAllAssetsAsync** N+1 on role resolution (should batch)
10. **MinIOAdapter.DownloadAsync** loads entire files into memory (MemoryStream)
11. **DotNetObjectReference leak** in AssetUpload per file upload
12. **Ancient cookie auth package** (2.3.9) referenced on .NET 9 — version conflict risk
13. **No cleanup for stale "uploading" assets** — orphaned records accumulate
14. **Keycloak role member query has no pagination** — misses users beyond page 1

### Medium (inconsistency / maintainability)

15. **String-typed enums** (status, role, scope) — no compile-time safety
16. **HttpContext in Application layer** — clean architecture violation in 4 interfaces
17. **ServiceResult<object>** return on `GetSharedContentAsync` — type safety lost
18. **Serilog vs Logging key mismatch** in Staging/Test appsettings — log levels ignored
19. **AssetCollectionRepository cache not invalidated** by `DeleteByCollectionAsync`
20. **CollectionAclRepository.SetAccessAsync** race condition on concurrent upsert
21. **No `:guid` route constraints** on asset/share endpoints — bad IDs reach handlers
22. **Admin SyncDeletedUsers defaults dryRun=false** — accidental real deletion
23. **CollectionMapper.ToDtoListAsync** N+1 on `IsRoleInheritedAsync`

### Low (code quality / polish)

24. Template pages still present (Counter.razor, Weather.razor, Home boilerplate)
25. 40+ hard-coded English strings alongside localized .resx system
26. Worker project identity crisis — sleeps forever, unused Hangfire packages
27. Duplicate API methods in AssetHubApiClient (Add/Update ACL are identical)
28. RoleHierarchy.AllRoles allocates on every call; ResolveRole is O(n)
29. GetHumanReadableSize() is presentation logic in domain entity
30. Missing router `<NotFound>` handler — unknown routes show blank page

## Test Coverage Summary

| Layer              | Coverage | Quality    |
|--------------------|----------|------------|
| Repositories       | Excellent| Real DB via Testcontainers, thorough edge cases |
| Services (18 impl) | **None** | Critical gap — entire orchestration layer untested |
| HTTP Endpoints     | **None** | WebApplicationFactory built but never used |
| UI Components      | 61%      | Good bUnit tests with MudBlazor patterns |
| UI Pages           | **None** | No bUnit page tests |
| E2E                | Good     | 14 specs, but silent-pass anti-pattern in many |

## What's Done Well

- Clean domain layer with zero dependencies
- ServiceResult pattern is ergonomic with implicit operators
- Centralized constants, cache keys, and role hierarchy
- Repository tests are thorough with real PostgreSQL containers
- Password/token generation is cryptographically sound
- Email template architecture follows clean template method pattern
- E2E suite has proper Page Object Model and API helpers
- Share token stored as SHA-256 hash — good security practice
- CurrentUser scoped service avoids HttpContext in most business logic
- Configuration POCOs follow consistent pattern with section name constants
