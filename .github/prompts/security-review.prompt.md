---
description: "Security-focused review of changed or specified files. Use when auditing auth, input handling, file operations, or sensitive data flows."
mode: "agent"
---
# AssetHub Security Review

Targeted security audit of AssetHub code changes. Focuses on OWASP Top 10, AssetHub-specific auth patterns, and the security conventions from `CLAUDE.md`.

## Scope

- If the user specifies files, review those.
- Otherwise, identify recently changed files across Infrastructure, Api, and Worker layers.

Read each file in full before reviewing.

## Checklist

### A01: Broken Access Control
- Every endpoint group has `.RequireAuthorization("Require…")`.
- Collection-scoped operations check `ICollectionAuthorizationService` before entity access.
- `CurrentUser` used for identity — never `HttpContext.User` directly.
- Role checks use `RoleHierarchy` predicate methods — no hardcoded role strings or int comparisons.
- Privilege escalation prevented: `HasSufficientLevel()` used when assigning roles.
- System admins bypass ACL checks explicitly via `currentUser.IsSystemAdmin`.

### A02: Cryptographic Failures
- No secrets hardcoded in source — placeholders in `appsettings.json`, real values from env/Docker secrets.
- Share tokens encrypted via ASP.NET Data Protection.
- Passwords hashed with BCrypt — never MD5/SHA-1.

### A03: Injection
- No `FromSqlRaw` / `FromSqlInterpolated` / string-concatenated SQL — LINQ only.
- PostgreSQL fuzzy search uses `EF.Functions.ILike`, not string interpolation.
- External process launches use `ProcessStartInfo.ArgumentList`, never a single command string.
- Filenames from user input pass through `FileHelpers.GetSafeFileName`.
- ZIP entries use sanitized names (no path traversal via `../`).
- No `.innerHTML` or equivalent in Blazor — use `MarkupString` only with sanitized content.

### A05: Security Misconfiguration
- Production `AllowedHosts` is a specific hostname, not `"*"`.
- Error responses use `ApiError` format — no stack traces leaked.
- Security headers applied via `UseSecurityHeaders()`.
- Debug/development features gated by environment.

### A07: Identification & Authentication Failures
- Session cookies: `HttpOnly`, `Secure`, `SameSite=Strict`.
- Rate limiting configured: per-user (200/min), SignalR (60 conn/min), anonymous shares (30/min), password attempts (10/5min).
- Anti-forgery: `.DisableAntiforgery()` only on API endpoints (JWT/same-origin), Blazor forms enforce via middleware.

### A08: Software & Data Integrity
- Upload pipeline: content-type allowlist → magic byte validation → ClamAV scan → size/batch limits → filename sanitization.
- No deserialization of untrusted types.

### AssetHub-Specific
- ACL/roles never cached globally — request-scoped dictionaries only.
- Presigned URLs not double-cached.
- Background services use `IServiceScopeFactory` — no scoped services injected into singletons.
- Audit events emitted for sensitive operations (downloads, deletions, permission changes).

## Report Format

```
## Security review — {scope}

### Critical
- [file.cs:42](…) — **A03 Injection** — `FromSqlRaw` with user-supplied input. Fix: use LINQ with `EF.Functions.ILike`.

### High / Medium / Low
…

### Clean
- A02 Cryptographic Failures — no hardcoded secrets ✓
- A07 Authentication — rate limiting configured ✓
```

Offer to fix vulnerabilities immediately. Critical findings should block the PR.
