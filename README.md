# AssetHub

**A modular, self-hosted digital asset management system built for teams who need enterprise features without vendor lock-in.**

AssetHub lets you organise images, videos, and documents into collections, control who sees what with per-collection roles, share files via password-protected links, and get automatic thumbnails and previews — all running on your own infrastructure with fully replaceable components.

Built with ASP.NET Core 9, Blazor Server, and a pluggable architecture. Swap out authentication, storage, database, or any service layer without changing application code.

---

## Who Is This For?

| Audience | Why AssetHub? |
|----------|---------------|
| **IT Teams** | Full control over infrastructure. No SaaS dependency, no per-seat licensing, no data leaving your network. |
| **Developers** | Clean separation of concerns with interface-driven services. Replace any component by implementing standard interfaces. |
| **Enterprise Architects** | Integrate with existing AD/LDAP via Keycloak federation. Connect to corporate S3 or Azure Blob. Use your existing PostgreSQL cluster. |
| **Small Teams** | One `docker compose up` gets you running. Zero configuration needed for evaluation. |
| **Security-Conscious Orgs** | Audit trail for every action. ClamAV malware scanning. Role-based access control. Self-hosted = your data never leaves your premises. |

---

## Features

| | |
|---|---|
| **Smart Collections** — Multi-collection assets with drag-and-drop upload | **Fine-grained Access** — Viewer / Contributor / Manager / Admin per collection |
| **Secure Sharing** — Password-protected, time-limited share links | **Background Processing** — Auto-generated thumbnails, previews, and video posters |
| **Enterprise Security** — ClamAV scanning, audit trail, RBAC, container hardening, Docker secrets | **Fully Modular** — Swap S3, Auth, DB, Email via clean interfaces |
| **Video Support** — Poster extraction via ffmpeg, inline playback | **Search** — Full-text trigram search across names, descriptions, and tags (GIN-indexed) |
| **Accessibility** — Skip-to-content, ARIA labels, keyboard navigation, multi-viewport | **Observability** — Prometheus, Grafana, Jaeger, structured logging |
| **Localisation** — Swedish and English, extensible via `.resx` files | **API Versioning** — Versioned API (`/api/v1/`) with request validation filters |
| **Zip Downloads** — Download collections or shared content as archives | **Admin Dashboard** — User management, share admin, paginated audit log |

---

## Screenshots

### Dashboard

<p>
  <img src="docs/screen%20shots/dashboard%201.png" alt="Dashboard overview" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/dashboard%202.png" alt="Dashboard storage chart" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/dashboard%203.png" alt="Dashboard activity" width="720" />
</p>

### Collections

<p>
  <img src="docs/screen%20shots/Collections.png" alt="Collections overview" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Collections%202.png" alt="Collection detail" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Collection%203.png" alt="Collection management" width="720" />
</p>

### Assets

<p>
  <img src="docs/screen%20shots/All%20assets.png" alt="All assets" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Asset%201.png" alt="Asset detail" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Asset%202.png" alt="Asset preview" width="720" />
</p>

### Sharing

<p>
  <img src="docs/screen%20shots/Access%20share%201.png" alt="Share link creation" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Access%20share%202.png" alt="Share access view" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Access%20share%203.png" alt="Share download" width="720" />
</p>

### Administration

<p>
  <img src="docs/screen%20shots/Admin%201.png" alt="Admin dashboard" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Admin%202.png" alt="User management" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Admin%203.png" alt="Share management" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Admin%204.png" alt="Audit log" width="720" />
</p>
<p>
  <img src="docs/screen%20shots/Admin%205.png" alt="Admin settings" width="720" />
</p>

---

## Quick Start

```bash
git clone <repository-url>
cd AssetHub

# Add hostnames to hosts file (required for OIDC same-site cookies)
# Windows: Add to C:\Windows\System32\drivers\etc\hosts
# Linux/Mac: Add to /etc/hosts
# 127.0.0.1 assethub.local keycloak.assethub.local

docker compose up --build
```

Open https://assethub.local:7252 and log in:

| User | Password | Role |
|------|----------|------|
| `mediaadmin` | `mediaadmin123` | Admin |
| `testuser` | `testuser123` | Viewer |

See [CREDENTIALS.md](CREDENTIALS.md) for all default passwords and connection strings.

---

## Architecture at a Glance

AssetHub follows **Clean Architecture** with strict dependency rules. Every external service is abstracted behind an interface.

```
Domain  <-  Application  <-  Infrastructure  <-  Api / Worker
                ^                                    ^
                Ui (Razor Class Library) ────────────┘
```

| Project | Purpose |
|---------|---------|
| `AssetHub.Domain` | Entities, enums, value objects — zero dependencies |
| `AssetHub.Application` | Service interfaces, DTOs, constants, business rules |
| `AssetHub.Infrastructure` | EF Core, MinIO, SMTP, ClamAV, Keycloak implementations |
| `AssetHub.Api` | ASP.NET Core host — Minimal APIs, auth, DI wiring, Blazor hosting |
| `AssetHub.Ui` | Blazor Server components, pages, layouts (Razor Class Library) |
| `AssetHub.Worker` | Hangfire background processor (separate container) |

For the full architecture diagram, layer details, modular component interfaces, and resilience patterns, see **[ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

---

## Modular Components

Every external dependency can be swapped by implementing a clean interface:

| Component | Default | Interface | Alternatives |
|-----------|---------|-----------|-------------|
| **Identity** | Keycloak 26 (OIDC) | `IKeycloakUserService` | Azure AD, Okta, Auth0 (OIDC standard; admin API needs adapter) |
| **Storage** | MinIO (S3 API) | `IMinIOAdapter` | AWS S3, Azure Blob, GCS |
| **Database** | PostgreSQL 16 | EF Core + Npgsql | SQL Server (requires migration rework) |
| **Email** | SMTP (Mailpit in dev) | `IEmailService` | SendGrid, AWS SES |
| **Malware Scan** | ClamAV (clamd TCP) | `IMalwareScannerService` | Any scanner SDK |
| **Jobs** | Hangfire + PostgreSQL | Hangfire abstraction | MassTransit, NServiceBus |
| **Tracing** | Jaeger (OTLP) | OpenTelemetry | Datadog, Honeycomb, Grafana Cloud |

Full interface definitions, implementation details, and replacement guides in **[ARCHITECTURE.md](docs/ARCHITECTURE.md#modular-components)**.

---

## Security Highlights

- **OIDC with PKCE** — Authorization Code flow, no implicit grant
- **Per-collection RBAC** — Viewer, Contributor, Manager, Admin roles
- **Rate limiting** — Global, SignalR, anonymous shares, password brute force
- **Upload validation** — Content-type allowlist, magic byte verification, ClamAV scanning, client-side pre-validation
- **Data encryption** — Share tokens and passwords encrypted at rest via ASP.NET Data Protection
- **Container hardening** — `cap_drop: ALL`, `no-new-privileges`, non-root users, read-only filesystems, PID limits
- **Docker secrets** — File-based secrets for all sensitive credentials in production
- **Network segmentation** — Isolated Docker networks for backend and observability services
- **CSP + security headers** — HSTS, X-Frame-Options, referrer policy, permissions policy

Full security documentation, RBAC matrix, and API reference in **[SECURITY.md](docs/SECURITY.md)**.

---

## Deployment

The production stack runs via Docker Compose with hardened containers, resource limits, and internal-only networking.

```bash
cp .env.template .env
docker compose -f docker/docker-compose.prod.yml up -d
```

Included reverse proxy configurations (Caddy and Nginx), backup/restore scripts, certificates, Keycloak configuration, CI/CD pipeline, monitoring, testing, and troubleshooting are covered in **[DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

---

## Testing

| Layer | Framework | Coverage |
|-------|-----------|----------|
| **Backend** (unit + integration) | xUnit, Testcontainers, Moq | 29 test files — repositories, endpoints, services, edge cases |
| **Components** (Blazor) | bUnit | 18 test files — dialogs, grids, helpers |
| **E2E** (browser) | Playwright (TypeScript) | 15 spec files — auth, CRUD, shares, admin, accessibility (Chromium, Firefox, WebKit, mobile) |

700+ .NET test methods across 53 files + 15 E2E spec files (x4 browsers). See **[DEPLOYMENT.md](docs/DEPLOYMENT.md#testing)** for commands and details.

---

## Project Status

**Production-ready.** All core features implemented and tested. The codebase builds with 0 errors and 0 warnings.

### Roadmap

- Office document preview (Word, Excel, PowerPoint)
- Video transcoding (HLS/DASH adaptive streaming)
- Group-based ACLs (Keycloak groups/roles)

---

## Documentation

| Document | Contents |
|----------|----------|
| **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** | System overview, project structure, layer dependencies, modular component interfaces, service reference, resilience patterns |
| **[SECURITY.md](docs/SECURITY.md)** | Authentication, RBAC, rate limiting, upload security, data protection, container hardening, network security, audit, API reference |
| **[DEPLOYMENT.md](docs/DEPLOYMENT.md)** | Quick start, certificates, environment config, container reference, production deployment, CI/CD, testing, monitoring, backups, troubleshooting |
| **[CREDENTIALS.md](CREDENTIALS.md)** | Default passwords, OAuth config, connection strings |
| **[CONTRIBUTING.md](CONTRIBUTING.md)** | Development setup, code style, PR guidelines |

---

## Contributing

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Ensure clean build: `dotnet build`
3. Run tests: `dotnet test`
4. Run E2E tests: `cd tests/E2E && npm test`
5. Open a Pull Request

See [CONTRIBUTING.md](CONTRIBUTING.md) for full guidelines.

---

## License

Apache License 2.0 — see [LICENSE](LICENSE) for details.
