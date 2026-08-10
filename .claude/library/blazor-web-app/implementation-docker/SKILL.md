---
name: implementation-docker
description: Container image and compose conventions — multi-stage builds, minimal pinned non-root base images, build-context hygiene, healthchecks, runtime-only secrets, and resource limits. Use when writing or reviewing a Dockerfile or compose file.
---

# Docker & containerization

## Principle (why)

A container image is an artifact shipped to production, so its conventions are security and operability rules, not style. The recurring failures are: secrets baked into a layer (permanent, extractable from the image), bloated/`latest`-tagged base images (large attack surface, non-reproducible builds), running as root (a container escape becomes host root), and missing health/limits (the orchestrator can't tell a hung container from a busy one, or stop one from starving its neighbors). Each convention below closes one of those.

## Pattern (what)

- **Multi-stage builds** — a build stage with the SDK/toolchain, a runtime stage with only the artifacts. The toolchain never ships.
- **Minimal, pinned base images** (`alpine`/`slim`, an explicit version). Never `latest` in production — builds must be reproducible.
- **Non-root `USER`** in every production image.
- **`.dockerignore`** excludes everything not needed at build: VCS metadata, dependency caches, build output, IDE files, tests.
- **`HEALTHCHECK`** so the orchestrator knows when the container is actually serving.
- **No secrets in image layers** — inject at runtime (orchestrator secrets, mounted files, env). A secret added then removed in a later layer is still in the image.
- **Combine related `RUN` steps and clean up in the same layer** — temp files removed in a separate layer still occupy the earlier one.
- **Resource limits** (CPU/memory) in compose/orchestration so one service can't starve the host.
- **Logs to stdout/stderr** — the platform collects them; don't write log files inside the container.

## Boundaries

- These are production-image rules. A local dev compose may relax some (bind-mounted source, looser limits) — but never the secret-handling ones.
- "Pin versions" means the base image and any fetched tool; application dependencies are pinned by their own lockfile, not the Dockerfile.
