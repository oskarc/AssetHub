# Deployment & Operations

This guide covers development setup, production deployment, container infrastructure, CI/CD, monitoring, testing, and troubleshooting.

---

## Table of Contents

- [Quick Start (Development)](#quick-start-development)
- [Prerequisites](#prerequisites)
- [Certificate Setup](#certificate-setup)
- [Environment Configuration](#environment-configuration)
- [Container Reference](#container-reference)
- [Production Deployment](#production-deployment)
  - [Minimal Production Stack](#minimal-production-stack)
  - [Reverse Proxy Setup](#reverse-proxy-setup)
  - [MinIO Setup](#minio-setup)
  - [Security Checklist](#security-checklist)
  - [Resource Limits](#resource-limits)
- [CI/CD](#cicd)
- [Testing](#testing)
- [Monitoring & Observability](#monitoring--observability)
- [Development](#development)
- [Backup Strategy](#backup-strategy)
- [Upgrade Procedures](#upgrade-procedures)
- [Troubleshooting](#troubleshooting)
- [ClamAV Notes](#clamav-notes)

---

## Quick Start (Development)

```bash
git clone <repository-url>
cd AssetHub

# Add the hostname to your hosts file (auth cookies are host-scoped to assethub.local)
# Windows: Add to C:\Windows\System32\drivers\etc\hosts
# Linux/Mac: Add to /etc/hosts
# 127.0.0.1 assethub.local

docker compose up --build   # 3 essential services (app, postgres, minio)
# or, with the optional scanner / test-email / telemetry dashboard:
# CLAMAV_ENABLED=true docker compose --profile full up --build
```

Open https://assethub.local:7252 and log in with a local account (ASP.NET Core Identity):

| User | Password | Role |
|------|----------|------|
| `admin` | set via `IDENTITY_ADMIN_PASSWORD` in `.env` | Admin |
| `testuser` | set via `TEST_VIEWER_PASSWORD` in `.env` (dev only) | Viewer |

The admin is seeded from `Identity:SeedAdmin` only against an empty user store; the
`testuser` viewer is seeded only when `Identity__SeedTestViewer=true` (the dev
compose sets it; production never does).

See [CREDENTIALS.md](../CREDENTIALS.md) for all default passwords and connection strings.

---

## Prerequisites

### System Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| CPU | 2 cores | 4+ cores |
| RAM | 4 GB | 8+ GB |
| Storage | 20 GB (OS + containers) | 100+ GB (scales with assets) |
| Docker | 24.0+ | Latest stable |
| Docker Compose | 2.20+ | Latest stable |

### Software Requirements

- **Docker Desktop** (Windows/Mac) or Docker Engine + Compose (Linux)
- **Git** for cloning the repository
- **OpenSSL** for certificate generation (included on Mac/Linux; Windows users can use Git Bash or WSL)
- **.NET 10 SDK** (for local development outside Docker)
- **Node.js** (for E2E tests)

### Production Network Requirements

- **Public DNS**: One hostname pointing to your server (e.g., `assethub.example.com`)
- **Ports**: 80 (HTTP redirect), 443 (HTTPS) — all other ports internal only
- **TLS Certificates**: From a trusted CA or Let's Encrypt

---

## Certificate Setup

AssetHub enforces TLS on all environments. You need valid certificates before starting the application.

### Development Certificates

For local development, generate a self-signed certificate that covers all local hostnames.

#### Option 1: Using OpenSSL (Recommended)

```bash
mkdir -p certs

# Generate a self-signed certificate valid for 365 days
openssl req -x509 -newkey rsa:4096 -sha256 -days 365 \
  -nodes -keyout certs/dev-cert.key -out certs/dev-cert.crt \
  -subj "/CN=assethub.local" \
  -addext "subjectAltName=DNS:localhost,DNS:assethub.local,DNS:api,DNS:api.assethub.local,IP:127.0.0.1"

# Convert to PFX format (required by Kestrel)
openssl pkcs12 -export -out certs/dev-cert.pfx \
  -inkey certs/dev-cert.key -in certs/dev-cert.crt \
  -passout pass:DevCertPassword123
```

#### Option 2: Using .NET dev-certs (Simpler, but less flexible)

```bash
dotnet dev-certs https --trust
dotnet dev-certs https -ep certs/dev-cert.pfx -p DevCertPassword123
```

> **Note:** The .NET dev-certs option only covers `localhost`. For the `assethub.local` hostname the app expects, use the OpenSSL method.

#### Trust the Certificate

**Windows:**
```powershell
Import-Certificate -FilePath certs\dev-cert.crt -CertStoreLocation Cert:\LocalMachine\Root
```

**macOS:**
```bash
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain certs/dev-cert.crt
```

**Linux:**
```bash
sudo cp certs/dev-cert.crt /usr/local/share/ca-certificates/assethub-dev.crt
sudo update-ca-certificates
```

#### Environment Variables

Set the certificate password in your `.env` file:

```dotenv
ASPNETCORE_Kestrel__Certificates__Default__Password=DevCertPassword123
```

#### Hosts File Configuration

| OS | File |
|----|------|
| Windows | `C:\Windows\System32\drivers\etc\hosts` |
| Mac/Linux | `/etc/hosts` |

```
127.0.0.1 assethub.local
```

### Production Certificates

For production, use certificates from a trusted Certificate Authority (Let's Encrypt, DigiCert, etc.).

#### Option 1: Reverse Proxy with TLS Termination (Recommended)

The recommended production setup uses a reverse proxy (Nginx, Caddy, Traefik) for TLS termination. See [Reverse Proxy Setup](#reverse-proxy-setup).

#### Option 2: Direct TLS on Application

```bash
openssl pkcs12 -export -out certs/prod-cert.pfx \
  -inkey privkey.pem -in fullchain.pem \
  -passout pass:YourSecurePassword
```

Update `docker-compose.prod.yml` to mount the certificate and set the password in environment variables.

---

## Environment Configuration

### Development

1. Copy the environment template:
   ```bash
   cp .env.template .env
   ```
2. Edit `.env` with development values (example passwords are fine for local dev)
3. Generate certificates (see above)
4. Start the stack:
   ```bash
   docker compose up --build
   ```

### Production

1. Copy and configure:
   ```bash
   cp .env.template .env
   # Edit .env with production values — use strong, unique passwords
   ```
2. Review and replace all `REPLACE_ME` values
3. Set up reverse proxy with TLS certificates
4. Start:
   ```bash
   docker compose -f docker/docker-compose.prod.yml up -d
   ```

---

## Container Reference

The default `docker compose up` starts the three **essential** services. ClamAV,
Mailpit, and the Aspire Dashboard are **optional**, gated behind the `full`
compose profile.

| Container | Purpose | Internal Port | Profile | Dev Exposed | Prod Exposed | Swappable? |
|-----------|---------|--------------|---------|-------------|--------------|------------|
| `assethub-api` | ASP.NET Core API + Blazor UI (single process — hosts all background work) | 7252 | essential | 127.0.0.1:7252 | 127.0.0.1:7252 | — (core) |
| `assethub-postgres` | Primary database (EF Core) | 5432 | essential | 127.0.0.1:5432 | not exposed | Any PostgreSQL instance |
| `assethub-minio` | S3-compatible object storage | 9000 / 9001 | essential | 127.0.0.1:9000, :9001 | not exposed | AWS S3 or any S3-compatible store |
| `assethub-clamav` | Malware scanning (clamd TCP) | 3310 | `full` | not exposed | not exposed | Set `ClamAV__Enabled=false` to disable |
| `assethub-mailpit` | Dev email capture | 8025 / 1025 | `full` | 127.0.0.1:8025, :1025 | not present | Configure `Email__*` for any SMTP relay |
| `assethub-aspire-dashboard` | Traces, metrics, and structured logs (OTLP) | 18888 / 18889 | `full` | 127.0.0.1:18888 | not exposed | Any OTLP-compatible backend |

There is **no** message broker, cache, or identity-provider container: messaging is
in-process (`System.Threading.Channels`), caching is in-memory (`HybridCache`), and
authentication is local ASP.NET Core Identity. The reshape removed RabbitMQ, Redis,
Keycloak, and the separate Worker.

> **Database note:** EF Core uses the Npgsql provider (PostgreSQL). Switching to SQL Server requires changing the provider and regenerating migrations.

### Minimal Production Stack

The compose stack is modular — point individual services at existing corporate infrastructure by overriding environment variables:

| Component | Config Keys | Notes |
|-----------|------------|-------|
| **PostgreSQL** | `ConnectionStrings__Postgres` | Standard Npgsql connection string. EF Core auto-migrates on startup. |
| **MinIO / S3** | `MinIO__Endpoint`, `MinIO__AccessKey`, `MinIO__SecretKey`, `MinIO__UseSSL`, `MinIO__PublicUrl` | MinIO SDK is S3-compatible. `PublicUrl` is the endpoint browsers use for presigned URLs. |
| **ClamAV** | `ClamAV__Enabled` | Set to `false` to skip malware scanning. |

> The API is the only application process — background work (media processing, ZIP building, retention sweeps) runs inside it. There is no separate Worker to deploy.

---

## Production Deployment

```bash
cp .env.template .env        # Fill in your passwords and domain
docker compose -f docker/docker-compose.prod.yml up -d
curl http://localhost:7252/health/ready   # Wait for "Healthy"
```

The production compose file includes:
- **Resource limits** — CPU, memory, and PID limits on every container (512 MB-1 GB memory, 100-200 PIDs)
- **Internal-only networking** — No ports exposed except the API on `127.0.0.1:7252`
- **Network segmentation** — Two isolated Docker networks: `backend` (data stores) and `observability` (monitoring). The API bridges both.
- **Health checks** with `start_period` for slow services (ClamAV: 5 min)
- **`restart: unless-stopped`** on all services
- **Container hardening** — cap_drop ALL, no-new-privileges, non-root users, read-only root filesystem, PID limits
- **Log rotation** — `json-file` driver with 50 MB x 5 files on every container
- **Docker secrets** — File-based secrets for sensitive credentials (`postgres_password`, `minio_root_password`, and `dp_cert` — the Data Protection key-ring certificate). Services use `_FILE` suffix environment variables (e.g., `POSTGRES_PASSWORD_FILE`)

### Reverse Proxy Setup

The production docker-compose does not include a reverse proxy. You must provide one externally. Ready-to-use configurations are included in the repository:

- **Caddy** (recommended): `docker/reverse-proxy/caddy/Caddyfile` — automatic TLS via Let's Encrypt, WebSocket support for Blazor SignalR, admin console IP restriction, security headers, 500 MB upload limit
- **Nginx**: `docker/reverse-proxy/nginx/nginx.conf` — manual TLS setup, WebSocket upgrade for `/_blazor`, admin IP restriction, security headers, 300s upload timeout

#### Using the Caddy Config

```bash
cp docker/reverse-proxy/caddy/Caddyfile /etc/caddy/Caddyfile
# Edit hostname: assethub.example.com
# Edit admin IP restrictions
caddy reload
```

Caddy automatically obtains and renews Let's Encrypt certificates.

#### Using the Nginx Config

```bash
cp docker/reverse-proxy/nginx/nginx.conf /etc/nginx/sites-available/assethub
ln -s /etc/nginx/sites-available/assethub /etc/nginx/sites-enabled/
# Edit hostnames, certificate paths, admin IP restrictions
nginx -t && systemctl reload nginx
```

### Initial Admin Account

There is no external identity provider to configure — authentication is local
ASP.NET Core Identity. On first startup against an **empty** database,
`IdentitySeeder` creates the four roles and one bootstrap admin from
`Identity:SeedAdmin` (`IDENTITY_ADMIN_USERNAME` / `IDENTITY_ADMIN_EMAIL` /
`IDENTITY_ADMIN_PASSWORD` in `.env`). It runs only when the user store is empty and
never overwrites an existing account, so set a strong `IDENTITY_ADMIN_PASSWORD`
before the first start.

After first login, sign in as the admin and create the rest of your users from the
admin console (`/admin`). Password policy (minimum length, complexity, lockout)
lives under the `Identity` settings section in `appsettings.json` — see
[CREDENTIALS.md](../CREDENTIALS.md).

### MinIO Setup

#### Bucket Creation

The application automatically creates the storage bucket on first startup. To configure manually:

1. Temporarily expose MinIO Console (`127.0.0.1:9001:9001` in compose)
2. Access `http://127.0.0.1:9001` with `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`
3. **Access Keys** > **Create access key** > Update `.env` with the generated keys
4. **Remove the port mapping** after configuration

#### Public Access (Optional)

To allow direct browser downloads from MinIO:
1. Add reverse proxy configuration for `minio.example.com`
2. Set `MINIO_PUBLIC_URL=https://minio.example.com` and `MINIO_PUBLIC_USE_SSL=true`

Leave `MINIO_PUBLIC_URL` empty to proxy all downloads through the API (simpler, recommended for most deployments).

### Security Checklist

Before going live, verify:

- [ ] All `REPLACE_ME` values in `.env` replaced with strong, unique passwords
- [ ] `.env` file permissions restricted: `chmod 600 .env`
- [ ] `IDENTITY_ADMIN_PASSWORD` set to a strong, unique value before first start
- [ ] HTTPS working on all public endpoints
- [ ] HTTP automatically redirects to HTTPS
- [ ] MinIO Console port (9001) not exposed externally
- [ ] PostgreSQL port (5432) not exposed externally
- [ ] Firewall configured (only ports 80/443 open to public)
- [ ] Backup script configured and tested
- [ ] Backup restore procedure tested
- [ ] Health monitoring configured
- [ ] Log rotation configured
- [ ] HSTS headers enabled in reverse proxy

### Resource Limits

Production docker-compose memory limits:

| Service | Memory Limit |
|---------|--------------|
| PostgreSQL | 512 MB |
| MinIO | 512 MB |
| API | 1 GB |
| ClamAV | 3 GB |
| Aspire Dashboard | 256 MB |

Adjust in `docker-compose.prod.yml`:
```yaml
deploy:
  resources:
    limits:
      memory: 2G
```

### Caching

Caching is entirely in-memory (`HybridCache` — an L1 `IMemoryCache` plus an
in-memory L2 via `AddDistributedMemoryCache`). There is no Redis to deploy, secure,
or monitor. Authorization decisions are never cached; they always hit the database.

### Single-Instance Design

AssetHub runs as a **single application instance**. Horizontal scaling of the app
is deliberately out of scope: the message bus (`System.Threading.Channels`), the
cache (`HybridCache`), and the Blazor SignalR circuit are all in-process, so a
second replica would not share state with the first. Scale the app **vertically**
(more CPU/RAM on the one API container); scale the data tier independently:

| Component | Scaling Strategy |
|-----------|-----------------|
| **PostgreSQL** | Use managed PostgreSQL (RDS, Cloud SQL) with read replicas, or Patroni for self-hosted HA. |
| **MinIO** | Use distributed MinIO (multi-node) or a managed S3-compatible service. |

#### Monitoring

- Set up alerts on the `/health/ready` endpoint — it checks PostgreSQL and MinIO (and ClamAV when enabled).
- Use the Aspire Dashboard (or a production OTLP backend like Grafana/Jaeger) for distributed tracing.

---

## CI/CD

GitHub Actions runs on every push and pull request to `main` and `develop`:

| Job | What It Does | Runs On |
|-----|-------------|---------|
| **build-and-test** | Restore, build (Release), run all .NET tests with Cobertura code coverage, upload results as artifacts | Every push and PR |
| **security-audit** | `dotnet list package --vulnerable --include-transitive` — fails the build on known CVEs | Every push and PR |
| **docker-build** | Builds the API image and scans it with Trivy for CRITICAL/HIGH OS and library vulnerabilities. Requires build-and-test + security-audit to pass first. | Push to `main` only |

---

## Testing

AssetHub has comprehensive test coverage across three layers:

### Backend Tests (`AssetHub.Tests`)

```bash
# Requires Docker running (for Testcontainers)
dotnet test tests/AssetHub.Tests/
```

Two test strategies in one project:

- **Integration tests** (repositories, endpoints, edge cases) — run against **real PostgreSQL** via Testcontainers and use `WebApplicationFactory<Program>` for full API stack testing with a custom auth handler
- **Unit tests** (services, helpers) — use Moq to isolate service logic from infrastructure

Backend coverage spans repositories (Asset, Collection, CollectionAcl,
AssetCollection, Share), the media/ZIP endpoints, the application services (upload
validation, deletion, shares, ACLs, ClamAV scanning, media processing, zip builds,
dashboards, audit logging, resilience), edge cases (authorization boundaries,
concurrency, multi-collection access, smart deletion, security), and helpers
(input validation, file magic-byte detection).

### Component Tests (`AssetHub.Ui.Tests`)

```bash
dotnet test tests/AssetHub.Ui.Tests/
```

bUnit component tests cover the grids, upload, collection tree, and the asset /
collection / share / user dialogs, plus unit tests for the display helpers, role
permissions, and the user-feedback service.

### E2E Tests (Playwright)

```bash
cd tests/E2E
npm install
npx playwright install chromium
npm test
```

Additional modes:
```bash
npm run test:headed   # Run with visible browser
npm run test:ui       # Playwright UI mode
```

The specs run against a Docker Compose stack across multiple browser targets
(Chromium, Firefox, WebKit, mobile Chrome). The journeys are **self-seeding** —
they create the collections and assets they need rather than depending on
pre-seeded data, and both the admin and `testuser` viewer are seeded from an empty
database, so the gate runs from nothing:

| Spec | Coverage |
|------|----------|
| `01-auth` | Local Identity login/logout flows |
| `02-navigation` | Page routing, sidebar |
| `03-collections` | Collection load and creation |
| `10-viewer-role` | Viewer restrictions (no upload, no admin) |
| `12-responsive-a11y` | Responsive layout, accessibility |
| `13-journeys` | Self-seeding collection + asset journeys |
| `14-language` | Swedish/English localisation switching |

### Writing Tests

- Place unit/integration tests in `tests/AssetHub.Tests/` mirroring the source structure
- Place Blazor component tests in `tests/AssetHub.Ui.Tests/`
- Place E2E tests in `tests/E2E/tests/specs/`
- All new features should include appropriate test coverage
- The suite runs roughly 700 .NET test methods plus the Playwright E2E specs across four browser targets.

---

## Monitoring & Observability

### Monitoring URLs (Development)

| Tool | URL | Purpose |
|------|-----|---------|
| Health check | https://assethub.local:7252/health | Readiness probe (PG + MinIO, and ClamAV when enabled) |
| MinIO Console | http://localhost:9001 | Storage usage, buckets |
| Aspire Dashboard | http://localhost:18888 | Traces, metrics, and structured logs (`full` profile) |
| Mailpit | http://localhost:8025 | Email capture (`full` profile) |

### Observability Architecture

```
  AssetHub API ──OTLP gRPC──> Aspire Dashboard (traces, metrics, logs)
                                   http://localhost:18888
```

The API exports traces and metrics to the .NET Aspire Dashboard via OTLP gRPC (`http://aspire-dashboard:18889`) when the `full` profile is running. The dashboard provides a built-in UI for viewing traces, metrics, and structured logs — no additional infrastructure required.

### OpenTelemetry Configuration

All settings under the `OpenTelemetry` section in `appsettings.json`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Enabled` | bool | `true` | Master switch for all OpenTelemetry |
| `ServiceName` | string | `"AssetHub"` | Service name in traces and metrics |
| `OtlpEndpoint` | string | `""` | OTLP collector endpoint (gRPC) |
| `SamplingRatio` | double | `1.0` | Trace sampling ratio (production: `0.1`) |
| `RecordExceptions` | bool | `true` | Include exception details in spans (production: `false`) |
| `StripQueryStrings` | bool | `false` | Remove query strings from traced URLs (production: `true`) |

#### Environment-Specific Defaults

| Setting | Development | Production |
|---------|-------------|------------|
| `SamplingRatio` | `1.0` (all traces) | `0.1` (10%) |
| `RecordExceptions` | `true` | `false` |
| `StripQueryStrings` | `false` | `true` |
| Dashboard UI port | exposed (18888) | not exposed |

### Security Considerations

- **OTLP transport** — Uses plain HTTP within Docker network. Switch to `https://` if the collector is on a different host.
- **Exception recording** — Disabled in production to prevent leaking connection strings, file paths, and PII in trace spans
- **Query string stripping** — Enabled in production to prevent leaking tokens, API keys, and user data

### Health Check Endpoints

| Service | Endpoint | Expected Response |
|---------|----------|-------------------|
| API | `https://your-host:7252/health` | `Healthy` |
| API (ready) | `https://your-host:7252/health/ready` | `Healthy` |
| MinIO | `http://minio:9000/minio/health/live` | HTTP 200 |

---

## Development

### Local Development (Outside Docker)

Prerequisites: .NET 10 SDK, PostgreSQL 16, MinIO.

```bash
dotnet restore
dotnet build
dotnet run --project src/AssetHub.Api/AssetHub.Api.csproj
```

### Docker Development

```bash
# Start all services
docker compose up --build

# Follow logs
docker compose logs -f

# Database shell
docker exec -it assethub-postgres psql -U postgres -d assethub

# Rebuild everything
docker compose down && docker compose up --build
```

---

## Backup Strategy

### Automated Backup & Restore Scripts

Ready-to-use scripts are included in `docker/`:

```bash
# Full backup (PostgreSQL + MinIO, gzipped, timestamped)
./docker/backup.sh                          # -> ./backups/<timestamp>/
./docker/backup.sh /mnt/nfs/assethub        # -> custom destination

# Restore from backup (shows metadata, asks for confirmation)
./docker/restore.sh ./backups/20260314_120000
```

The backup script dumps all PostgreSQL databases and archives the MinIO `/data` volume. Both scripts validate that required containers are running before starting and provide clear progress output. The restore script extracts MinIO to a temp location before swapping, so a failed extraction won't leave you with an empty volume.

### Manual Backup

#### PostgreSQL

```bash
# Full database dump
docker exec assethub-postgres pg_dumpall -U assethub > backup_$(date +%Y%m%d).sql

# Restore
cat backup.sql | docker exec -i assethub-postgres psql -U assethub
```

#### MinIO Data

MinIO data is stored in the `miniodata` Docker volume. Back up the volume or use MinIO's `mc mirror`:

```bash
mc alias set local http://localhost:9000 $MINIO_ACCESS_KEY $MINIO_SECRET_KEY
mc mirror local/assethub-assets /path/to/backup/
```

> The PostgreSQL dump also contains the ASP.NET Data Protection key ring
> (`DataProtectionKeys` table) and all user accounts, so the Postgres + MinIO
> backup pair is a complete backup.

---

## Upgrade Procedures

1. Pull latest changes
2. Review changelog for breaking changes
3. Back up the database: `docker exec assethub-postgres pg_dumpall -U assethub > backup_$(date +%Y%m%d).sql`
4. Stop the stack: `docker compose down`
5. Rebuild images: `docker compose build`
6. Start the stack: `docker compose up -d`
7. Monitor logs: `docker compose logs -f api`
8. Verify health: `curl -f http://127.0.0.1:7252/health`

Database migrations are controlled by the `Database:AutoMigrate` setting. In development (default `true`), migrations run automatically on startup. In production (default `false`), pending migrations are logged as warnings and must be applied manually:

```bash
# Apply migrations manually before starting the production stack
docker exec assethub-api dotnet ef database update
```

If a migration fails, restore from backup.

### Rollback

```bash
docker compose -f docker/docker-compose.prod.yml down
cat backup_YYYYMMDD.sql | docker exec -i assethub-postgres psql -U assethub
git checkout v1.2.3  # or specific tag/commit
docker compose -f docker/docker-compose.prod.yml up -d --build
```

---

## Troubleshooting

### General

| Symptom | Solution |
|---------|----------|
| App won't start | Check `docker compose logs assethub-api`. Usually PostgreSQL or MinIO not ready yet. |
| Can't log in | Add `127.0.0.1 assethub.local` to your hosts file and browse to `https://assethub.local:7252` — the auth cookie is host-scoped to that name, so `localhost` won't hold a session. |
| Uploads fail | Check MinIO console at http://localhost:9001. Bucket should be auto-created. |
| Health check fails | Hit `/health/ready` to see which dependency is down. |
| Certificate errors | Trust the self-signed certificate in your OS certificate store. See [Certificate Setup](#certificate-setup). |
| ClamAV slow to start | First boot downloads virus definitions (2-5 min). Health check has a 5-minute start period. |
| Thumbnails not generating | Check API logs: `docker compose logs assethub-api`. Media processing runs in that container, so ImageMagick/ffmpeg errors appear there. |

### Certificate Errors

- **"The remote certificate is invalid"** — Certificate not trusted. Follow trust instructions in [Certificate Setup](#certificate-setup).
- **"Certificate does not match hostname"** — Certificate SAN doesn't include the hostname. Regenerate with correct hostnames.

### Sign-in Issues

- **Redirected back to the login page** — the auth cookie is host-scoped (`__Host-`/`assethub.local`); make sure you're browsing over HTTPS to `assethub.local`, not `localhost`.
- **No admin account** — the bootstrap admin seeds only against an empty user store. If the store already has users, no admin is created; sign in with an existing account or reset via the `Identity:SeedAdmin` path against a fresh database.

### Observability Issues

- **No traces in Aspire Dashboard** — Check `OpenTelemetry:Enabled` and `OtlpEndpoint`. Low sampling ratio means many requests needed before a trace appears.
- **Dashboard shows no data** — Verify the OTLP endpoint (`http://aspire-dashboard:18889`) is reachable from the API container on the observability network, and that you started the stack with the `full` profile.

### Log Aggregation

Production uses structured JSON logging at Warning level:
```bash
docker logs assethub-api --tail 100 -f
```

Configure log rotation in `/etc/docker/daemon.json` or per-service in the compose file.

---

## ClamAV Notes

### First Startup

ClamAV downloads virus definitions on first start (2-5 minutes). The container health check has a 5-minute `start_period`.

### Disabling

ClamAV is **off by default** — it lives in the `full` compose profile and is only
started when you pass `--profile full`, and scanning stays off unless
`CLAMAV_ENABLED=true`. To run without it, simply use the default stack (`docker
compose up`). To keep the scanner container but skip scanning:

```dotenv
CLAMAV_ENABLED=false
```

### Virus Definition Updates

ClamAV automatically updates definitions via the built-in `freshclam` daemon approximately every 2 hours.
