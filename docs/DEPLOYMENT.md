# AssetHub Deployment Guide

This guide covers setting up AssetHub for both development and production environments.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Certificate Setup](#certificate-setup)
  - [Development Certificates](#development-certificates)
  - [Production Certificates](#production-certificates)
- [Reverse Proxy Setup](#reverse-proxy-setup)
- [Environment Configuration](#environment-configuration)
- [Initial Keycloak Configuration](#initial-keycloak-configuration)
- [MinIO Setup](#minio-setup)
- [Health Check Endpoints](#health-check-endpoints)
- [Backup Strategy](#backup-strategy)
- [Log Aggregation](#log-aggregation)
- [Troubleshooting](#troubleshooting)
- [Upgrade Procedures](#upgrade-procedures)
- [Security Checklist](#security-checklist)
- [Resource Limits](#resource-limits)
- [ClamAV Notes](#clamav-notes)

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

Create the certificate with Subject Alternative Names for all local services:

```bash
# Create the certs directory if it doesn't exist
mkdir -p certs

# Generate a self-signed certificate valid for 365 days
openssl req -x509 -newkey rsa:4096 -sha256 -days 365 \
  -nodes -keyout certs/dev-cert.key -out certs/dev-cert.crt \
  -subj "/CN=assethub.local" \
  -addext "subjectAltName=DNS:localhost,DNS:assethub.local,DNS:keycloak.assethub.local,DNS:keycloak,IP:127.0.0.1"

# Convert to PFX format (required by Kestrel and Keycloak)
openssl pkcs12 -export -out certs/dev-cert.pfx \
  -inkey certs/dev-cert.key -in certs/dev-cert.crt \
  -passout pass:DevCertPassword123
```

#### Option 2: Using .NET dev-certs (Simpler, but less flexible)

```bash
# Generate and trust the .NET development certificate
dotnet dev-certs https --trust

# Export to PFX format
dotnet dev-certs https -ep certs/dev-cert.pfx -p DevCertPassword123
```

> **Note:** The .NET dev-certs option only covers `localhost`. For Keycloak integration with proper hostname validation, use the OpenSSL method.

#### Trust the Certificate

After generating the certificate, you must trust it on your system to avoid browser warnings:

**Windows:**
```powershell
# Import into Trusted Root Certification Authorities
Import-Certificate -FilePath certs\dev-cert.crt -CertStoreLocation Cert:\LocalMachine\Root
```

**macOS:**
```bash
# Add to system keychain and trust
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain certs/dev-cert.crt
```

**Linux:**
```bash
# Copy to CA certificates and update
sudo cp certs/dev-cert.crt /usr/local/share/ca-certificates/assethub-dev.crt
sudo update-ca-certificates
```

#### Environment Variables

Set the certificate password in your `.env` file:

```dotenv
# Certificate password (must match what you used in the openssl/dotnet command)
KC_HTTPS_KEY_STORE_PASSWORD=DevCertPassword123
ASPNETCORE_Kestrel__Certificates__Default__Password=DevCertPassword123
```

#### Hosts File Configuration

Add these entries to your hosts file:

| OS | File |
|----|------|
| Windows | `C:\Windows\System32\drivers\etc\hosts` |
| Mac/Linux | `/etc/hosts` |

```
127.0.0.1 assethub.local
127.0.0.1 keycloak.assethub.local
127.0.0.1 keycloak
```

---

### Production Certificates

For production, use certificates from a trusted Certificate Authority (Let's Encrypt, DigiCert, etc.).

#### Option 1: Reverse Proxy with TLS Termination (Recommended)

The recommended production setup uses a reverse proxy (Nginx, Caddy, Traefik) for TLS termination. The proxy handles certificates, and internal traffic between containers uses HTTP.

See the [Reverse Proxy Setup](#reverse-proxy-setup) section below.

#### Option 2: Direct TLS on Application

If you prefer the application to handle TLS directly:

1. Obtain certificates from your CA
2. Convert to PFX format:
   ```bash
   openssl pkcs12 -export -out certs/prod-cert.pfx \
     -inkey privkey.pem -in fullchain.pem \
     -passout pass:YourSecurePassword
   ```
3. Update `docker-compose.prod.yml` to mount the certificate
4. Set the password in your environment variables

---

## Reverse Proxy Setup

The production docker-compose does not include a reverse proxy. You must provide one externally.

### Nginx Example

Create `nginx.conf`:

```nginx
upstream assethub {
    server 127.0.0.1:7252;
}

server {
    listen 80;
    server_name assethub.example.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name assethub.example.com;

    ssl_certificate /etc/letsencrypt/live/assethub.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/assethub.example.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers off;

    client_max_body_size 500M;

    location / {
        proxy_pass http://assethub;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
}
```

### Caddy Example (Auto-HTTPS)

Create `Caddyfile`:

```caddy
assethub.example.com {
    reverse_proxy 127.0.0.1:7252
}
```

Caddy automatically obtains and renews Let's Encrypt certificates.

---

## Environment Configuration

### Development

1. Copy the environment template:
   ```bash
   cp .env.template .env
   ```

2. Edit `.env` and set development values (example passwords are fine for local dev):
   ```dotenv
   POSTGRES_PASSWORD=devpassword
   KEYCLOAK_ADMIN_PASSWORD=admin
   KEYCLOAK_DB_PASSWORD=keycloak
   KEYCLOAK_CLIENT_SECRET=your-client-secret
   # ... see .env.template for all variables
   ```

3. Generate certificates (see [Certificate Setup](#certificate-setup))

4. Start the stack:
   ```bash
   docker compose up --build
   ```

### Production

1. Copy and configure environment:
   ```bash
   cp .env.template .env
   # Edit .env with production values - use strong, unique passwords
   ```

2. Review and set all `REPLACE_ME` values in `.env`

3. Set up reverse proxy with TLS certificates

4. Start the stack:
   ```bash
   docker compose -f docker/docker-compose.prod.yml up -d
   ```

---

## Initial Keycloak Configuration

After first startup, complete these steps:

### 1. Rotate Admin Credentials

Log into Keycloak admin console (`https://keycloak.example.com/admin`):
1. Login with `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD`
2. Click your username (top right) → **Manage account** → **Signing in**
3. Change the password immediately

### 2. Verify Realm Import

The `media` realm is auto-imported on first startup:
1. Switch to the **media** realm (dropdown in top-left)
2. Verify **Clients** → `assethub-app` exists and settings are correct
3. Verify **Users** → `mediaadmin` and `testuser` exist
4. Update user passwords if using default values

### 3. Create Admin Service Account (Recommended)

For the application to manage users via Keycloak Admin API:

1. Go to **Clients** → **Create client**
2. Set **Client ID**: `assethub-admin`
3. Enable **Client authentication**: On
4. Enable **Service accounts roles**: On
5. **Save**
6. Go to **Credentials** tab → Copy the **Client secret**
7. Go to **Service accounts roles** tab → **Assign role**:
   - Filter by clients → `realm-management`
   - Assign: `manage-users`, `view-users`, `query-users`
8. Update `.env`:
   ```dotenv
   KEYCLOAK_ADMIN_CLIENT_ID=assethub-admin
   KEYCLOAK_ADMIN_CLIENT_SECRET=<copied-secret>
   ```

### 4. Configure Password Policies

In **Realm settings** → **Authentication** → **Policies**:
- Minimum length: 12
- Special characters: 1
- Uppercase: 1
- Digits: 1
- Password history: 5

---

## Health Check Endpoints

| Service | Endpoint | Expected Response |
|---------|----------|-------------------|
| API | `https://your-host:7252/health` | `Healthy` |
| API (ready) | `https://your-host:7252/health/ready` | `Healthy` |
| Keycloak | `https://keycloak:8443/health/ready` | `{"status": "UP"}` |
| MinIO | `http://minio:9000/minio/health/live` | HTTP 200 |

---

## Backup Strategy

### PostgreSQL

```bash
# Full database dump
docker exec assethub-postgres pg_dumpall -U assethub > backup_$(date +%Y%m%d).sql

# Restore
cat backup.sql | docker exec -i assethub-postgres psql -U assethub
```

### MinIO Data

MinIO data is stored in the `miniodata` Docker volume. Back up the volume or use MinIO's `mc mirror` command:

```bash
# Install MinIO client
mc alias set local http://localhost:9000 $MINIO_ACCESS_KEY $MINIO_SECRET_KEY

# Mirror to backup location
mc mirror local/assethub-assets /path/to/backup/
```

### Keycloak

Keycloak data is stored in PostgreSQL (in the `keycloak` database). It's included in the PostgreSQL backup above.

---

## Troubleshooting

### Certificate Errors

**"The remote certificate is invalid"** — The certificate is not trusted by the system. Follow the trust instructions in [Certificate Setup](#certificate-setup).

**"Certificate does not match hostname"** — The certificate's SAN (Subject Alternative Name) doesn't include the hostname you're accessing. Regenerate with correct hostnames.

### Keycloak Token Validation Failures

**"Issuer validation failed"** — The `Keycloak:Authority` setting doesn't match the issuer in tokens. Ensure the URL is exactly as users access Keycloak (including port if non-standard).

### Container Health Check Failures

Check container logs:
```bash
docker compose logs api
docker compose logs keycloak
```

ClamAV can take 2-5 minutes to start on first boot while downloading virus definitions.

---

## Upgrade Procedures

1. Pull latest changes
2. Review changelog for breaking changes
3. Back up the database: `docker exec assethub-postgres pg_dumpall -U assethub > backup_$(date +%Y%m%d).sql`
4. Stop the stack: `docker compose down`
5. Rebuild images: `docker compose build`
6. Start the stack: `docker compose up -d`
7. Monitor logs for migration issues: `docker compose logs -f api`
8. Verify health: `curl -f http://127.0.0.1:7252/health`

Database migrations run automatically on startup. If a migration fails, restore from backup.

### Rollback Procedure

```bash
# Stop current version
docker compose -f docker/docker-compose.prod.yml down

# Restore database (if needed)
cat backup_YYYYMMDD.sql | docker exec -i assethub-postgres psql -U assethub

# Checkout previous version
git checkout v1.2.3  # or specific tag/commit

# Rebuild and start
docker compose -f docker/docker-compose.prod.yml up -d --build
```

---

## Log Aggregation

### Default Logging

Production uses structured JSON logging at Warning level. View logs:

```bash
docker logs assethub-api --tail 100 -f
```

### Docker Log Rotation

Configure in `/etc/docker/daemon.json` or per-service:

```yaml
services:
  api:
    logging:
      driver: json-file
      options:
        max-size: "50m"
        max-file: "5"
```

### Centralized Logging (Optional)

For Loki, Elasticsearch, or similar systems, configure a log shipping driver:

```yaml
services:
  api:
    logging:
      driver: "fluentd"
      options:
        fluentd-address: "localhost:24224"
        tag: "assethub.api"
```

---

## MinIO Setup

### Bucket Creation

The application automatically creates the storage bucket on first startup. To configure manually:

1. Temporarily expose MinIO Console:
   ```yaml
   # Add to docker-compose.prod.yml
   minio:
     ports:
       - "127.0.0.1:9001:9001"
   ```

2. Access `http://127.0.0.1:9001` with `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`

3. Create an application access key:
   - Navigate to **Access Keys** → **Create access key**
   - Update `.env` with the generated `MINIO_ACCESS_KEY` and `MINIO_SECRET_KEY`

4. **Remove the port mapping** after configuration.

### Public Access (Optional)

To allow direct browser downloads from MinIO (bypassing the API):

1. Add reverse proxy configuration for `minio.example.com`
2. Set `MINIO_PUBLIC_URL=https://minio.example.com`
3. Set `MINIO_PUBLIC_USE_SSL=true`

Leave `MINIO_PUBLIC_URL` empty to proxy all downloads through the API (simpler, recommended for most deployments).

---

## Security Checklist

Before going live, verify:

- [ ] All `REPLACE_ME` values in `.env` replaced with strong, unique passwords
- [ ] `.env` file permissions restricted: `chmod 600 .env`
- [ ] HTTPS working on all public endpoints
- [ ] HTTP automatically redirects to HTTPS
- [ ] Keycloak admin password changed from initial bootstrap value
- [ ] MinIO Console port (9001) not exposed externally
- [ ] PostgreSQL port (5432) not exposed externally  
- [ ] Keycloak port (8080) only exposed via reverse proxy
- [ ] Firewall configured (only ports 80/443 open to public)
- [ ] Backup script configured and tested
- [ ] Backup restore procedure tested
- [ ] Health monitoring configured
- [ ] Log rotation configured
- [ ] HSTS headers enabled in reverse proxy

---

## Resource Limits

Production docker-compose sets these memory limits:

| Service | Memory Limit |
|---------|--------------|
| PostgreSQL | 512 MB |
| MinIO | 512 MB |
| Keycloak | 768 MB |
| API | 1 GB |
| Worker | 1 GB |
| ClamAV | 1 GB |

Adjust in `docker-compose.prod.yml` based on your workload:

```yaml
deploy:
  resources:
    limits:
      memory: 2G  # Increase as needed
```

---

## ClamAV Notes

### First Startup

ClamAV downloads virus definitions on first start, which takes 2-5 minutes depending on network speed. The container health check has a 5-minute `start_period` to accommodate this.

### Disabling Malware Scanning

If ClamAV is not needed:

```dotenv
CLAMAV_ENABLED=false
```

Remove or comment out the `clamav` service in docker-compose and the `depends_on` reference in the `api` service.

### Virus Definition Updates

ClamAV automatically updates definitions via the built-in `freshclam` daemon. Updates occur approximately every 2 hours.

