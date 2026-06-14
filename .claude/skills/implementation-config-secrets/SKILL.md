---
name: implementation-config-secrets
description: Strongly-typed configuration settings with fail-fast validation for critical infrastructure, and a no-hardcoded-secrets discipline with environment-aware secret sourcing. Use when adding a settings class, wiring config validation, or handling any credential/secret.
---

# Configuration & secrets

## Principle (why)

Two failure modes motivate this. First, **misconfiguration should fail at startup, loudly**, not at the first request that happens to need the missing value — a service that boots "healthy" then 500s on its first real call is worse than one that refuses to boot. So critical infrastructure config is validated on start. Second, **a secret in source is a secret leaked** — it's in history forever, visible to everyone with repo access, and copied into every fork and CI cache. Secrets come from the environment, never the codebase, and the *absence* of a required secret fails loudly rather than silently falling back to a default (a default credential is a vulnerability, not a convenience — see `implementation-csharp-conventions`).

## Pattern (what)

**Strongly-typed settings classes.**
- Each settings group is a class with a `const string SectionName`, bound from configuration, with DataAnnotations on the fields (`[Required]`, range, etc.).
- **Critical infrastructure** settings (the datastore, the broker, the cache, the identity provider, object storage) are registered with **validate-on-start** so a missing/invalid value crashes the boot with a clear message. Optional features (email, image processing) bind without validate-on-start — their absence degrades a feature, it doesn't break the system.

```csharp
public sealed class ExampleSettings
{
    public const string SectionName = "Example";
    [Required] public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
}
// services.AddOptions<ExampleSettings>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart();
```

**Secrets.**
- Never hardcode a secret, and never provide a default-credential fallback (`?? "guest"`). A required secret that's missing must make validation fail — that's the signal something is unconfigured.
- Environment variables override configuration via the platform's nested-key mapping (`__` → `:`).
- Production sources secrets from a secret store / file-based secrets, not plaintext environment variables baked into an image or compose file.

## Boundaries

- "Critical → validate-on-start" is the dividing line: if the system can't function at all without it, validate it on start; if a feature can be disabled, don't.
- Validate-on-start checks *presence and shape*, not liveness — it won't catch a wrong-but-well-formed connection string. That's what health checks and the first real connection are for.
