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
  - [Initial Keycloak Configuration](#initial-keycloak-configuration)
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

# Add hostnames to hosts file (required for OIDC same-site cookies)
# Windows: Add to C:\Windows\System32\drivers\etc\hosts
# Linux/Mac: Add to /etc/hosts
# 127.0.0.1 assethub.local keycloak.assethub.local keycloak

docker compose up --build
```

Open https://assethub.local:7252 and log in:

| User | Password | Role |
|------|----------|------|
| `mediaadmin` | `mediaadmin123` | Admin |
| `testuser` | `testuser123` | Viewer |

See [CREDENTIALS.md](../CREDENTIALS.md) for all default passwords, OAuth config, and connection strings.

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
- **.NET 9 SDK** (for local development outside Docker)
- **Node.js** (for E2E tests)

### Production Network Requirements

- **Public DNS**: Two hostnames pointing to your server (e.g., `assethub.example.com`, `keycloak.example.com`)
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
  -addext "subjectAltName=DNS:localhost,DNS:assethub.local,DNS:api,DNS:api.assethub.local,DNS:keycloak,DNS:keycloak.assethub.local,IP:127.0.0.1"

# Convert to PFX format (required by Kestrel and Keycloak)
openssl pkcs12 -export -out certs/dev-cert.pfx \
  -inkey certs/dev-cert.key -in certs/dev-cert.crt \
  -passout pass:DevCertPassword123
```

#### Option 2: Using .NET dev-certs (Simpler, but less flexible)

```bash
dotnet dev-certs https --trust
dotnet dev-certs https -ep certs/dev-cert.pfx -p DevCertPassword123
```

> **Note:** The .NET dev-certs option only covers `localhost`. For Keycloak integration with proper hostname validation, use the OpenSSL method.

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
KC_HTTPS_KEY_STORE_PASSWORD=DevCertPassword123
ASPNETCORE_Kestrel__Certificates__Default__Password=DevCertPassword123
```

#### Hosts File Configuration

| OS | File |
|----|------|
| Windows | `C:\Windows\System32\drivers\etc\hosts` |
| Mac/Linux | `/etc/hosts` |

```
127.0.0.1 assethub.local
127.0.0.1 keycloak.assethub.local
127.0.0.1 keycloak
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

| Container | Purpose | Internal Port | Dev Exposed | Prod Exposed | Swappable? |
|-----------|---------|--------------|-------------|--------------|------------|
| `assethub-api` | ASP.NET Core API + Blazor UI | 7252 | 127.0.0.1:7252 | 127.0.0.1:7252 | — (core) |
| `assethub-worker` | Hangfire background processor (ImageMagick, ffmpeg, zip) | — | — | — | — (core) |
| `assethub-postgres` | Primary database (EF Core + Hangfire) | 5432 | 127.0.0.1:5432 | not exposed | Any PostgreSQL instance |
| `assethub-minio` | S3-compatible object storage | 9000 / 9001 | 127.0.0.1:9000, :9001 | not exposed | AWS S3 or any S3-compatible store |
| `assethub-keycloak` | OIDC identity provider + Admin API | 8080 / 8443 | 127.0.0.1:8080, :8443 | not exposed | Requires adapter rewrites (see note) |
| `assethub-clamav` | Malware scanning (clamd TCP) | 3310 | not exposed | not exposed | Set `ClamAV__Enabled=false` to disable |
| `assethub-aspire-dashboard` | Traces, metrics, and structured logs (OTLP) | 18888 / 18889 | 127.0.0.1:18888 | not exposed | Any OTLP-compatible backend |
| `assethub-redis` | Distributed cache (L2) + SignalR backplane | 6379 | 127.0.0.1:6379 | not exposed | Any Redis 7+ or managed Redis service |
| `assethub-mailpit` | Dev email capture (dev only) | 8025 / 1025 | 127.0.0.1:8025, :1025 | not present | Configure `Email__*` for any SMTP relay |

> **Keycloak note:** OIDC authentication is standard and works with any compliant provider. However, the application also calls the Keycloak Admin REST API for user management. Swapping Keycloak for Azure AD, Okta, or Auth0 requires new implementations of `IKeycloakUserService` and `IUserLookupService`.
>
> **Database note:** EF Core uses the Npgsql provider (PostgreSQL). Switching to SQL Server requires changing the provider and regenerating migrations.

### Minimal Production Stack

The compose stack is modular — point individual services at existing corporate infrastructure by overriding environment variables:

| Component | Config Keys | Notes |
|-----------|------------|-------|
| **PostgreSQL** | `ConnectionStrings__Postgres` | Standard Npgsql connection string. EF Core auto-migrates on startup. |
| **MinIO / S3** | `MinIO__Endpoint`, `MinIO__AccessKey`, `MinIO__SecretKey`, `MinIO__UseSSL`, `MinIO__PublicUrl` | MinIO SDK is S3-compatible. `PublicUrl` is the endpoint browsers use for presigned URLs. |
| **ClamAV** | `ClamAV__Enabled` | Set to `false` to skip malware scanning. |

> Both the API and the Worker are required for a functional deployment.

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
- **Network segmentation** — Two isolated Docker networks: `backend` (data stores) and `observability` (monitoring). API/Worker bridge both.
- **Health checks** with `start_period` for slow services (ClamAV: 5 min, Keycloak: 2 min)
- **`restart: unless-stopped`** on all services
- **Container hardening** — cap_drop ALL, no-new-privileges, non-root users, read-only root filesystem, PID limits
- **Log rotation** — `json-file` driver with 50 MB x 5 files on every container
- **Keycloak production mode** — `KC_HOSTNAME_STRICT`, `KC_PROXY_HEADERS: xforwarded`, HTTPS required
- **Docker secrets** — File-based secrets for all sensitive credentials (`postgres_password`, `keycloak_admin_password`, `keycloak_db_password`, `minio_root_password`, `keycloak_client_secret`). Services use `_FILE` suffix environment variables (e.g., `POSTGRES_PASSWORD_FILE`)

### Reverse Proxy Setup

The production docker-compose does not include a reverse proxy. You must provide one externally. Ready-to-use configurations are included in the repository:

- **Caddy** (recommended): `docker/reverse-proxy/caddy/Caddyfile` — automatic TLS via Let's Encrypt, WebSocket support for Blazor SignalR, Keycloak proxying, admin console IP restriction, security headers, 500 MB upload limit
- **Nginx**: `docker/reverse-proxy/nginx/nginx.conf` — manual TLS setup, WebSocket upgrade for `/_blazor`, Keycloak proxying, admin IP restriction, security headers, 300s upload timeout

#### Using the Caddy Config

```bash
cp docker/reverse-proxy/caddy/Caddyfile /etc/caddy/Caddyfile
# Edit hostnames: assethub.example.com, keycloak.example.com
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

### Initial Keycloak Configuration

After first startup:

#### 1. Rotate Admin Credentials

Log into Keycloak admin console (`https://keycloak.example.com/admin`), click your username > **Manage account** > **Signing in**, and change the password immediately.

#### 2. Verify Realm Import

The `media` realm is auto-imported on first startup:
1. Switch to the **media** realm (dropdown in top-left)
2. Verify **Clients** > `assethub-app` exists
3. Verify **Users** > `mediaadmin` and `testuser` exist
4. Update user passwords if using default values

#### 3. Create Admin Service Account (Recommended)

For the application to manage users via Keycloak Admin API:

1. **Clients** > **Create client** > Client ID: `assethub-admin`
2. Enable **Client authentication** and **Service accounts roles**
3. **Save** > **Credentials** tab > Copy the **Client secret**
4. **Service accounts roles** > **Assign role** > Filter by `realm-management` > Assign: `manage-users`, `view-users`, `query-users`
5. Update `.env`:
   ```dotenv
   KEYCLOAK_ADMIN_CLIENT_ID=assethub-admin
   KEYCLOAK_ADMIN_CLIENT_SECRET=<copied-secret>
   ```

#### 4. Configure Password Policies

In **Realm settings** > **Authentication** > **Policies**:
- Minimum length: 12, Special characters: 1, Uppercase: 1, Digits: 1, Password history: 5

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
- [ ] `REDIS_PASSWORD` set to a strong, unique value (not the dev default)
- [ ] HTTPS working on all public endpoints
- [ ] HTTP automatically redirects to HTTPS
- [ ] Keycloak admin password changed from initial bootstrap value
- [ ] MinIO Console port (9001) not exposed externally
- [ ] PostgreSQL port (5432) not exposed externally
- [ ] Redis port (6379) not exposed externally (internal network only)
- [ ] Keycloak port (8080) only exposed via reverse proxy
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
| Keycloak | 768 MB |
| API | 1 GB |
| Worker | 1 GB |
| ClamAV | 1 GB |
| Redis | 256 MB (200 MB maxmemory) |

Adjust in `docker-compose.prod.yml`:
```yaml
deploy:
  resources:
    limits:
      memory: 2G
```

### Redis Configuration

Redis is configured as a **pure ephemeral cache** (no persistence) with the following server-side settings:

| Setting | Value | Purpose |
|---------|-------|---------|
| `requirepass` | `${REDIS_PASSWORD}` | Authentication — prevents unauthorized access |
| `maxmemory` | `200mb` | Caps memory usage below the 256 MB container limit |
| `maxmemory-policy` | `allkeys-lru` | Evicts least-recently-used keys when memory is full |
| `save ""` | (disabled) | No RDB snapshots — data is ephemeral |
| `appendonly no` | (disabled) | No AOF log — data is ephemeral |

The application gracefully handles Redis unavailability by falling back to in-memory caching. Authorization decisions are **never cached in Redis** — they always hit the database.

### High Availability (HA) Production Considerations

The default Docker Compose setup runs a single instance of each service. For production environments requiring high availability, consider the following.

#### Redis HA

The single-instance Redis in `docker-compose.prod.yml` is sufficient for small-to-medium deployments because:
- AssetHub treats Redis as a **pure cache** (no persistence, no critical state)
- If Redis goes down, the app falls back to per-instance in-memory caching (L1 only)
- Cache misses cause extra database queries but no data loss or functional failures

For larger deployments where cache availability matters:

1. **Redis Sentinel** (recommended for most HA needs)
   - Deploy 1 primary + 2 replicas + 3 Sentinel instances
   - Provides automatic failover (typically < 30 seconds)
   - Update connection string: `redis-sentinel:26379,serviceName=mymaster,password=...`
   - StackExchange.Redis supports Sentinel natively

2. **Redis Cluster** (for very large datasets)
   - Shards data across multiple nodes for horizontal scaling
   - More complex to operate — only needed if cache data exceeds single-node memory
   - Requires `StackExchange.Redis` cluster-aware configuration

3. **Managed Redis** (simplest HA path)
   - AWS ElastiCache, Azure Cache for Redis, or GCP Memorystore
   - Handles replication, failover, patching, and monitoring automatically
   - Update `Redis__ConnectionString` to point to the managed endpoint
   - Enable TLS in the connection string: `managed-redis.example.com:6380,ssl=true,password=...`

#### TLS for Redis

Within a single-host Docker network, unencrypted Redis traffic is acceptable (network-isolated). For multi-host or cloud deployments:

- **Managed Redis**: Enable the provider's TLS option and add `ssl=true` to the connection string
- **Self-hosted**: Configure Redis with `tls-port`, `tls-cert-file`, and `tls-key-file`, or use a sidecar proxy (stunnel, envoy)
- **Connection string**: `redis:6380,ssl=true,password=...,abortConnect=false`

#### Scaling the Application

| Component | Scaling Strategy |
|-----------|-----------------|
| **API** | Run multiple replicas behind a load balancer. Redis already serves as the SignalR backplane and shared L2 cache. |
| **Worker** | Run multiple replicas. Hangfire coordinates job distribution via PostgreSQL — no conflicts. |
| **PostgreSQL** | Use managed PostgreSQL (RDS, Cloud SQL) with read replicas, or Patroni for self-hosted HA. |
| **MinIO** | Use distributed MinIO (multi-node) or a managed S3-compatible service. |
| **Keycloak** | Run multiple replicas behind a load balancer. Keycloak uses the shared PostgreSQL for session/state. |

#### Monitoring in HA

- Set up alerts on the `/health/ready` endpoint — it checks PostgreSQL, MinIO, Keycloak, ClamAV, and Redis
- Monitor Redis memory usage (`INFO memory`) — if `used_memory` approaches `maxmemory`, consider increasing the limit or reviewing cache TTLs
- Monitor cache hit rates (`INFO stats` — `keyspace_hits` vs `keyspace_misses`) to validate caching effectiveness
- Use the Aspire Dashboard (or a production OTLP backend like Grafana/Jaeger) for distributed tracing across replicas

---

## CI/CD

GitHub Actions runs on every push and pull request to `main` and `develop`:

| Job | What It Does | Runs On |
|-----|-------------|---------|
| **build-and-test** | Restore, build (Release), run all .NET tests with Cobertura code coverage, upload results as artifacts | Every push and PR |
| **security-audit** | `dotnet list package --vulnerable --include-transitive` — fails the build on known CVEs | Every push and PR |
| **docker-build** | Builds API + Worker images, scans both with Trivy for CRITICAL/HIGH OS and library vulnerabilities. Requires build-and-test + security-audit to pass first. | Push to `main` only |

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

Coverage across 33 test files:

| Category | Files | What's Tested |
|----------|-------|---------------|
| Repositories | 5 | Asset, Collection, CollectionAcl, AssetCollection, Share CRUD |
| Endpoints | 5 | Asset, Collection, Share, Admin, Dashboard API authorization and responses |
| Services | 12 | Upload validation, deletion, shares, ACLs, ClamAV scanning, media processing, Keycloak, zip builds, dashboards, audit logging, external service resilience |
| Edge cases | 6 | Authorization boundaries, ACL inheritance, concurrency, multi-collection access, smart deletion, security (rate limiting, CORS, header injection) |
| Helpers | 2 | Input validation, file magic byte detection |

### Component Tests (`AssetHub.Ui.Tests`)

```bash
dotnet test tests/AssetHub.Ui.Tests/
```

20 test files covering:
- **bUnit component tests** (17 files) — AssetGrid, AssetUpload, CollectionTree, CreateShareDialog, EditAssetDialog, ManageAccessDialog, AddToCollectionDialog, CreateCollectionDialog, LanguageSwitcher, EmptyState, BulkAssetActionsDialog, BulkCollectionActionsDialog, CreateUserDialog, DeleteAssetDialog, ShareInfoDialog, SharePasswordDialog, and more
- **Unit tests** (3 files) — AssetDisplayHelpers, RolePermissions, UserFeedbackService

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

15 spec files running against a full Docker Compose stack across 4 browser targets (Chromium, Firefox, WebKit, mobile Chrome):

| Spec | Coverage |
|------|----------|
| `01-auth` | Keycloak OIDC login/logout flows |
| `02-navigation` | Page routing, sidebar, breadcrumbs |
| `03-collections` | Collection CRUD |
| `04-assets` | Upload, preview, metadata editing |
| `05-shares` | Share creation, password protection, public access |
| `06-admin` | Admin panel operations, user management |
| `07-all-assets` | Admin asset search and filtering |
| `08-api` | Direct API endpoint testing |
| `09-acl` | Per-collection role assignment and enforcement |
| `10-viewer-role` | Viewer restrictions (no upload, no delete, no share) |
| `11-edge-cases` | Boundary conditions and error handling |
| `12-responsive-a11y` | Responsive layout, accessibility |
| `13-workflows` | Multi-step user workflows |
| `14-language` | Swedish/English localisation switching |
| `15-ui-features` | Grid/list toggle, pagination, clipboard |

### Writing Tests

- Place unit/integration tests in `tests/AssetHub.Tests/` mirroring the source structure
- Place Blazor component tests in `tests/AssetHub.Ui.Tests/`
- Place E2E tests in `tests/E2E/tests/specs/`
- All new features should include appropriate test coverage
- Total: 700+ .NET test methods across 53 test files + 15 E2E spec files (x4 browsers)

---

## Monitoring & Observability

### Monitoring URLs (Development)

| Tool | URL | Purpose |
|------|-----|---------|
| Health check | https://assethub.local:7252/health/ready | Readiness probe (PG + MinIO + Keycloak + ClamAV) |
| Hangfire | https://assethub.local:7252/hangfire | Job queues, processing status |
| Aspire Dashboard | http://localhost:18888 | Traces, metrics, and structured logs |
| Keycloak Admin | https://keycloak.assethub.local:8443/admin | Users, sessions, clients |
| MinIO Console | http://localhost:9001 | Storage usage, buckets |
| Mailpit | http://localhost:8025 | Email capture (dev only) |

### Observability Architecture

```
  AssetHub API    ──OTLP gRPC──> Aspire Dashboard (traces, metrics, logs)
  AssetHub Worker ──OTLP gRPC──>        │
                                   http://localhost:18888
```

Both the API and Worker export traces and metrics to the .NET Aspire Dashboard via OTLP gRPC (`http://aspire-dashboard:18889`). The dashboard provides a built-in UI for viewing traces, metrics, and structured logs — no additional infrastructure required.

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
| Keycloak | `https://keycloak:8443/health/ready` | `{"status": "UP"}` |
| MinIO | `http://minio:9000/minio/health/live` | HTTP 200 |

---

## Development

### Local Development (Outside Docker)

Prerequisites: .NET 9 SDK, PostgreSQL 16, MinIO, Keycloak with the `media` realm.

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

The backup script dumps all PostgreSQL databases (which includes Keycloak data) and archives the MinIO `/data` volume. Both scripts validate that required containers are running before starting and provide clear progress output. The restore script extracts MinIO to a temp location before swapping, so a failed extraction won't leave you with an empty volume.

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

#### Keycloak

Keycloak data is stored in PostgreSQL (in the `keycloak` database). It's included in the PostgreSQL backup above.

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
| App won't start | Check `docker compose logs assethub-api`. Usually PostgreSQL or Keycloak not ready yet. |
| Can't log in | Add `127.0.0.1 assethub.local keycloak.assethub.local` to hosts file. Token issuer must match. |
| Uploads fail | Check MinIO console at http://localhost:9001. Bucket should be auto-created. |
| Health check fails | Hit `/health/ready` to see which dependency is down. |
| Certificate errors | Trust the self-signed certificate in your OS certificate store. See [Certificate Setup](#certificate-setup). |
| ClamAV slow to start | First boot downloads virus definitions (2-5 min). Health check has a 5-minute start period. |
| Thumbnails not generating | Check Worker logs: `docker compose logs assethub-worker`. ImageMagick/ffmpeg errors will appear there. |

### Certificate Errors

- **"The remote certificate is invalid"** — Certificate not trusted. Follow trust instructions in [Certificate Setup](#certificate-setup).
- **"Certificate does not match hostname"** — Certificate SAN doesn't include the hostname. Regenerate with correct hostnames.

### Keycloak Issues

- **"Issuer validation failed"** — `Keycloak:Authority` doesn't match the issuer in tokens. Ensure the URL matches exactly, including port.
- **Token validation failures** — Check that the Keycloak realm and client configuration match the application settings.

### Observability Issues

- **No traces in Aspire Dashboard** — Check `OpenTelemetry:Enabled` and `OtlpEndpoint`. Low sampling ratio means many requests needed before a trace appears.
- **Dashboard shows no data** — Verify the OTLP endpoint (`http://aspire-dashboard:18889`) is reachable from the API/Worker containers on the observability network.

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

```dotenv
CLAMAV_ENABLED=false
```

Remove or comment out the `clamav` service in docker-compose and the `depends_on` reference in the `api` service.

### Virus Definition Updates

ClamAV automatically updates definitions via the built-in `freshclam` daemon approximately every 2 hours.
