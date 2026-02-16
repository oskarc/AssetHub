# AssetHub — Deployment Guide

From a fresh clone to a running instance in three commands.

---

## Table of Contents

1. [One-Command Install](#1-one-command-install)
2. [What Happens Automatically](#2-what-happens-automatically)
3. [First-Time Configuration](#3-first-time-configuration)
4. [Reverse Proxy & TLS](#4-reverse-proxy--tls)
5. [Verify Everything Works](#5-verify-everything-works)
6. [Day-2 Operations](#6-day-2-operations)
7. [Security Hardening](#7-security-hardening)
8. [Troubleshooting](#8-troubleshooting)
9. [Environment Variable Reference](#9-environment-variable-reference)

---

## 1. One-Command Install

### Prerequisites

| Requirement | Minimum |
|-------------|---------|
| Docker Engine | 24+ with Compose v2 (`docker compose version`) |
| RAM | 3 GB free |
| Disk | 10 GB + storage for uploads |
| DNS | Domain(s) pointing at the server (e.g. `assethub.example.com`, `keycloak.example.com`) |

### Steps

```bash
# 1. Clone
git clone <repository-url> && cd AssetHub

# 2. Configure — fill in every REPLACE_ME value
cp .env.template .env && nano .env

# 3. Launch
docker compose -f docker/docker-compose.prod.yml up -d
```

That's it. The application will:

- Start PostgreSQL, MinIO, Keycloak, the API, and the Hangfire worker
- **Auto-apply** all database migrations (including `pg_trgm` for search)
- **Auto-create** the MinIO storage bucket
- **Auto-import** the Keycloak realm (OIDC client, test users)

Watch startup progress:

```bash
docker compose -f docker/docker-compose.prod.yml logs -f api
```

Wait for the health check to pass:

```bash
curl -s http://localhost:7252/health/ready | python3 -m json.tool
```

### Create your first admin user

1. Open Keycloak Admin: `https://<KEYCLOAK_HOSTNAME>/admin`
2. Select **media** realm → **Users** → **Add user**
3. Set username, email, first/last name
4. **Credentials** tab → set a strong password (uncheck "Temporary")
5. **Role Mapping** → Assign **admin** realm role
6. Open `https://assethub.example.com` → Log in

---

## 2. What Happens Automatically

The application provisions everything on startup — no manual steps required:

| Task | Detail |
|------|--------|
| **Database migrations** | EF Core migrations run automatically on startup. No `dotnet ef database update` needed. |
| **pg_trgm extension** | Created automatically for trigram full-text search. |
| **MinIO bucket** | Created if it doesn't exist. No `mc mb` command needed. |
| **Keycloak realm** | Imported from `keycloak/import/media-realm.json` on first boot via `--import-realm`. |
| **Data Protection keys** | Persisted to PostgreSQL automatically. No key management required. |

### Health Check Endpoints

Two endpoints are available for monitoring and load balancers:

| Endpoint | Purpose | What it checks |
|----------|---------|----------------|
| `GET /health` | **Liveness** probe | Is the process alive? Always returns 200. |
| `GET /health/ready` | **Readiness** probe | PostgreSQL + MinIO + Keycloak all reachable and working. |

Example `/health/ready` response:

```json
{
  "status": "Healthy",
  "duration": "142ms",
  "checks": [
    { "name": "postgresql", "status": "Healthy", "duration": "12ms" },
    { "name": "minio", "status": "Healthy", "duration": "45ms", "description": "Bucket 'assethub' is accessible." },
    { "name": "keycloak", "status": "Healthy", "duration": "85ms", "description": "OIDC discovery endpoint returned 200." }
  ]
}
```

The Docker Compose file uses `/health` as the container health check, so `docker compose ps` shows health status.

---

## 3. First-Time Configuration

### 3.1 Fill in `.env`

The most important variables to set:

| Variable | What to put | How to generate |
|----------|-------------|-----------------|
| `APP_BASE_URL` | `https://assethub.example.com` | Your public URL |
| `KEYCLOAK_HOSTNAME` | `keycloak.example.com` | Must match the issuer in JWT tokens |
| `POSTGRES_PASSWORD` | Strong random password | `openssl rand -base64 32` |
| `KEYCLOAK_ADMIN_PASSWORD` | Strong random password | `openssl rand -base64 32` |
| `KEYCLOAK_CLIENT_SECRET` | From Keycloak UI | See step 3.2 |
| `KEYCLOAK_ADMIN_API_PASSWORD` | Strong random password | `openssl rand -base64 32` |
| `MINIO_ROOT_USER` / `PASSWORD` | Strong random | `openssl rand -base64 32` |
| `MINIO_ACCESS_KEY` / `SECRET` | Strong random | `openssl rand -base64 32` |

Everything else has sensible defaults. See [Section 9](#9-environment-variable-reference) for the full reference.

### 3.2 Get the Keycloak Client Secret

After first start, the OIDC client is auto-created but you need to grab the secret:

1. Open `https://<KEYCLOAK_HOSTNAME>/admin` → select **media** realm
2. **Clients** → `assethub-app` → **Credentials** tab → copy **Client Secret**
3. Paste into `.env` as `KEYCLOAK_CLIENT_SECRET`
4. Verify **Valid Redirect URIs** includes `https://assethub.example.com/signin-oidc`
5. Restart: `docker compose -f docker/docker-compose.prod.yml restart api`

### 3.3 Create the Admin Service Account

For the "Create User from Admin UI" feature:

1. **media** realm → **Users** → **Add user**: `svc-assethub`
2. Set a strong password (not temporary) matching `KEYCLOAK_ADMIN_API_PASSWORD` in `.env`
3. **Role Mapping** → assign `realm-management` → `manage-users` and `view-users`

### 3.4 MinIO Application Access Key (optional)

The bucket is auto-created by the app, but you may want a dedicated access key scoped to the bucket:

```bash
docker exec -it assethub-minio mc alias set local http://localhost:9000 $MINIO_ROOT_USER $MINIO_ROOT_PASSWORD
docker exec -it assethub-minio mc admin user add local $MINIO_ACCESS_KEY $MINIO_SECRET_KEY
docker exec -it assethub-minio mc admin policy attach local readwrite --user $MINIO_ACCESS_KEY
```

### 3.5 Remove Test Users (production)

Delete or disable the default test users shipped in the realm import:

- `testuser` / `testuser123`
- `mediaadmin` / `mediaadmin123`

---

## 4. Reverse Proxy & TLS

The production compose binds the API to `127.0.0.1:7252` — you need a reverse proxy for TLS termination.

### Option A: Caddy (easiest — automatic HTTPS)

```Caddyfile
assethub.example.com {
    reverse_proxy localhost:7252
}

keycloak.example.com {
    reverse_proxy localhost:8080
}
```

```bash
sudo caddy run --config /etc/caddy/Caddyfile
```

### Option B: Nginx + Certbot

```nginx
server {
    listen 443 ssl http2;
    server_name assethub.example.com;

    ssl_certificate     /etc/letsencrypt/live/assethub.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/assethub.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:7252;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 500M;
    }
}

server {
    listen 443 ssl http2;
    server_name keycloak.example.com;

    ssl_certificate     /etc/letsencrypt/live/keycloak.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/keycloak.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Option C: Traefik

Add labels to `api` and `keycloak` services in the compose file:

```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.assethub.rule=Host(`assethub.example.com`)"
  - "traefik.http.routers.assethub.tls.certresolver=letsencrypt"
  - "traefik.http.services.assethub.loadbalancer.server.port=7252"
```

> **Important**: Blazor Server uses WebSockets (SignalR). Your proxy **must** pass `Upgrade` + `Connection: upgrade` headers (Nginx config above does this; Caddy does it automatically).

---

## 5. Verify Everything Works

After starting, run through this checklist:

```bash
# 1. All containers running and healthy?
docker compose -f docker/docker-compose.prod.yml ps

# 2. Readiness probe passes? (all integrations OK)
curl -s https://assethub.example.com/health/ready | python3 -m json.tool

# 3. Keycloak OIDC discovery works?
curl -s https://keycloak.example.com/realms/media/.well-known/openid-configuration | head -5
```

Then in the browser:

1. Open `https://assethub.example.com` → redirected to Keycloak login
2. Log in with your admin user → returned to the app
3. Create a collection → upload an image → thumbnail appears within seconds
4. Create a share link → open in an incognito window → content loads
5. Check Hangfire dashboard at `https://assethub.example.com/hangfire`

---

## 6. Day-2 Operations

### Backup

```bash
# PostgreSQL (schedule daily, retain 30 days)
docker exec assethub-postgres pg_dump -U ${POSTGRES_USER} ${POSTGRES_DB} \
  | gzip > backup_$(date +%Y%m%d_%H%M%S).sql.gz

# MinIO data
docker exec assethub-minio mc mirror local/${MINIO_BUCKET_NAME} /backup/

# Keycloak realm
docker exec assethub-keycloak /opt/keycloak/bin/kc.sh export --dir /tmp/export --realm media
docker cp assethub-keycloak:/tmp/export/media-realm.json ./keycloak-backup.json
```

### Restore

```bash
gunzip -c backup_20260208.sql.gz | docker exec -i assethub-postgres psql -U ${POSTGRES_USER} ${POSTGRES_DB}
```

### Upgrade

```bash
git pull origin main

# Backup first
docker exec assethub-postgres pg_dump -U ${POSTGRES_USER} ${POSTGRES_DB} > pre-upgrade.sql

# Rebuild — migrations run automatically on start
docker compose -f docker/docker-compose.prod.yml up -d --build

# Confirm everything is healthy
curl -s https://assethub.example.com/health/ready
```

> Check `.env.template` diff for any new required variables between versions.

### Scale Workers

```bash
docker compose -f docker/docker-compose.prod.yml up -d --scale worker=3
```

### PostgreSQL Performance Tuning

For large deployments, tune in `postgresql.conf` or via Docker environment variables:

- `shared_buffers` — 25% of available RAM
- `work_mem` — 4-16 MB
- `effective_cache_size` — 50-75% of available RAM
- Connection pool size in connection string: `Maximum Pool Size=50;`
- Monitor slow queries with `pg_stat_statements`

---

## 7. Security Hardening

Complete before going live:

- [ ] All `REPLACE_ME` values in `.env` replaced with strong random passwords
- [ ] Test users (`testuser`, `mediaadmin`) deleted from Keycloak
- [ ] HTTPS enabled on all public endpoints (app + Keycloak)
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (set in compose — enforces HTTPS metadata)
- [ ] Keycloak `assethub-app` client: **Client Authentication ON** (confidential)
- [ ] Keycloak token lifetimes reviewed (access: 5-15 min, refresh: 30 min-8 hrs)
- [ ] Hangfire dashboard only accessible to authenticated admin users
- [ ] Firewall: only 80/443 exposed; 5432, 9000, 8080 internal only
- [ ] PostgreSQL, MinIO ports NOT exposed in `docker-compose.prod.yml` (commented out by default)
- [ ] PostgreSQL user is `assethub` (not `postgres`) — default in `.env.template`
- [ ] Client secret rotated after initial setup
- [ ] MinIO access key scoped to the application bucket only
- [ ] PostgreSQL SSL enabled if database is on a separate host

---

## 8. Troubleshooting

### Quick Diagnosis

```bash
# Check all container status
docker compose -f docker/docker-compose.prod.yml ps

# Check API health with details
curl -s http://localhost:7252/health/ready | python3 -m json.tool

# Recent API logs
docker logs assethub-api --tail 50
```

### Common Issues

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| API won't start: `Keycloak:Authority is required` | Missing env var | Check `.env` has `KEYCLOAK_HOSTNAME` |
| API won't start: `Npgsql.PostgresException` | Wrong DB password | Match `POSTGRES_PASSWORD` in `.env` |
| API unhealthy: minio check fails | MinIO not ready yet | Wait 30s, then restart: `docker compose restart api` |
| Migrations fail on startup | Schema conflict | Check `docker logs assethub-api` for the migration error |
| Can't log in: redirect loop | Issuer mismatch | `curl -s https://keycloak.example.com/realms/media/.well-known/openid-configuration \| grep issuer` — must match `Keycloak__Authority` |
| Can't log in: invalid redirect | Redirect URI wrong | Keycloak → Clients → `assethub-app` → Valid Redirect URIs must include `.../signin-oidc` |
| Can't log in: unauthorized_client | Client secret mismatch | Compare Keycloak Credentials tab with `KEYCLOAK_CLIENT_SECRET` in `.env` |
| Thumbnails not generating | Worker down or OOM | `docker logs assethub-worker --tail 30`; exit code 139 = OOM, increase memory |
| Bucket not auto-created | Startup error | `docker logs assethub-api \| grep -i bucket` |

### CORS (if MinIO is publicly exposed)

If presigned download URLs point directly at MinIO (via `MINIO_PUBLIC_URL`), configure CORS:

```bash
cat > /tmp/cors.json << 'EOF'
{
  "CORSRules": [{
    "AllowedOrigins": ["https://assethub.example.com"],
    "AllowedMethods": ["GET", "PUT"],
    "AllowedHeaders": ["*"],
    "MaxAgeSeconds": 3600
  }]
}
EOF
docker exec -i assethub-minio mc cors set local/assethub < /tmp/cors.json
```

### S3 Migration

MinIO SDK is S3-compatible. To switch to AWS S3: change `MinIO__Endpoint` to `s3.amazonaws.com`, set `MinIO__UseSSL=true`, use IAM credentials. No code changes needed.

---

## 9. Environment Variable Reference

All variables defined in `.env.template` with descriptions:

| Variable | Required | Default | Purpose |
|----------|:--------:|---------|---------|
| **General** | | | |
| `TZ` | | `Europe/Stockholm` | Container timezone |
| `APP_BASE_URL` | **Yes** | — | Public URL for share links and OIDC redirects |
| **PostgreSQL** | | | |
| `POSTGRES_DB` | **Yes** | `assethub` | Database name |
| `POSTGRES_USER` | **Yes** | `assethub` | Database user |
| `POSTGRES_PASSWORD` | **Yes** | — | Database password |
| **Keycloak** | | | |
| `KEYCLOAK_ADMIN` | **Yes** | `admin` | Master realm admin |
| `KEYCLOAK_ADMIN_PASSWORD` | **Yes** | — | Master realm admin password |
| `KEYCLOAK_HOSTNAME` | **Yes** | — | Public hostname (embedded in JWT tokens) |
| `KEYCLOAK_CLIENT_ID` | **Yes** | `assethub-app` | OIDC client ID |
| `KEYCLOAK_CLIENT_SECRET` | **Yes** | — | OIDC client secret |
| `KEYCLOAK_ADMIN_API_USERNAME` | **Yes** | — | Service account for user management |
| `KEYCLOAK_ADMIN_API_PASSWORD` | **Yes** | — | Service account password |
| **MinIO** | | | |
| `MINIO_ROOT_USER` | **Yes** | — | MinIO root user |
| `MINIO_ROOT_PASSWORD` | **Yes** | — | MinIO root password |
| `MINIO_ACCESS_KEY` | **Yes** | — | App-level access key |
| `MINIO_SECRET_KEY` | **Yes** | — | App-level secret key |
| `MINIO_BUCKET_NAME` | | `assethub` | Storage bucket name |
| `MINIO_PUBLIC_URL` | | — | Public URL for presigned downloads (blank = proxy through app) |
| `MINIO_PUBLIC_USE_SSL` | | `true` | Use HTTPS for public MinIO URLs |
| **Email** | | | |
| `EMAIL_ENABLED` | | `false` | Enable email notifications |
| `EMAIL_SMTP_HOST` | | — | SMTP server |
| `EMAIL_SMTP_PORT` | | `587` | SMTP port |
| `EMAIL_SMTP_USERNAME` | | — | SMTP user |
| `EMAIL_SMTP_PASSWORD` | | — | SMTP password |
| `EMAIL_USE_SSL` | | `true` | SMTP TLS |
| `EMAIL_FROM_ADDRESS` | | — | Sender email |
| `EMAIL_FROM_NAME` | | `AssetHub` | Sender name |
| **Tuning** | | | |
| `APP_MAX_UPLOAD_SIZE_MB` | | `500` | Max upload size (MB) |
| `APP_DEFAULT_PAGE_SIZE` | | `50` | Pagination size |
| `IMAGE_THUMBNAIL_WIDTH` | | `200` | Thumbnail width (px) |
| `IMAGE_THUMBNAIL_HEIGHT` | | `200` | Thumbnail height (px) |
| `IMAGE_MEDIUM_WIDTH` | | `800` | Medium rendition width (px) |
| `IMAGE_MEDIUM_HEIGHT` | | `800` | Medium rendition height (px) |
| `IMAGE_JPEG_QUALITY` | | `85` | JPEG quality (1-100) |
| `VIDEO_POSTER_FRAME_SECONDS` | | `5` | Poster capture time (seconds) |
| `VIDEO_POSTER_WIDTH` | | `800` | Poster width (px) |
| `VIDEO_POSTER_QUALITY` | | `5` | Poster quality |
