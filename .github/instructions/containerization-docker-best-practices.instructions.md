---
applyTo: '**/Dockerfile,**/Dockerfile.*,**/*.dockerfile,**/docker-compose*.yml,**/docker-compose*.yaml,**/compose*.yml,**/compose*.yaml'
description: 'AssetHub Docker conventions. Applies to Dockerfiles and compose files in this project.'
---
# Docker Conventions — AssetHub

## Reference files
Before editing, read the existing Dockerfiles and compose files:
- `docker/Dockerfile` — API multi-stage build (.NET 9)
- `docker/Dockerfile.Worker` — Worker multi-stage build
- `docker/Dockerfile.ClamAV`, `docker/Dockerfile.RabbitMQ` — patched infra images
- `docker/docker-compose.yml` — development stack
- `docker/docker-compose.prod.yml` — production overrides

## AssetHub image rules
- **Multi-stage builds** for all application images (build → runtime).
- **Base images**: `mcr.microsoft.com/dotnet/aspnet:9.0-alpine` (runtime), `mcr.microsoft.com/dotnet/sdk:9.0-alpine` (build). Pin minor versions — no `:latest`.
- **Non-root `USER`** in all production images.
- **`HEALTHCHECK`** instruction in every Dockerfile.
- **No secrets in layers** — use runtime secrets (Docker Secrets, env vars with `__` → `:` mapping).
- **Combine `RUN` commands** and clean up temp files in the same layer.
- **`.dockerignore`** excludes `.git`, `node_modules`, build artifacts, IDE files, test files.

## Compose conventions
- **Resource limits** (`cpu_limits`, `memory_limits`) on every service.
- **Named volumes** for persistent data (Postgres, MinIO, Redis, RabbitMQ).
- **Internal networks** for backend services; only reverse proxy and API exposed.
- **Logs to `STDOUT`/`STDERR`** — no file-based logging inside containers.

## Security scanning
CI pipeline (`.github/workflows/ci.yml`) builds and scans images with **Trivy** on the main branch. Critical vulnerabilities block the build.

## Quick checklist
- [ ] Multi-stage build separates SDK from runtime?
- [ ] Pinned base image version (not `:latest`)?
- [ ] Non-root `USER` defined?
- [ ] `HEALTHCHECK` present?
- [ ] No secrets or credentials in any layer?
- [ ] `.dockerignore` up to date?
- [ ] Resource limits in compose?