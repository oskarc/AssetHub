# AssetHub - Digital Asset Management System

A modern, scalable digital asset management (DAM) system built with ASP.NET Core 9, Keycloak, and MinIO. Features hierarchical collections, role-based access control, automated media processing, and public share links.

## 🎯 Features

### Core Functionality
- **Hierarchical Collections**: Organize assets in nested folders with parent-child relationships
- **Role-Based Access Control**: Fine-grained permissions (viewer/contributor/manager/admin) on collections
- **Media Upload**: Multi-file upload with progress tracking and automatic processing
- **Image Processing**: Automatic thumbnail generation (ImageMagick)
- **Video Support**: Metadata extraction and poster frame generation (ffmpeg)
- **Share Links**: Time-limited public share tokens with optional password protection
- **Audit Logging**: Complete history of uploads, shares, and downloads
- **Full-Text Search**: Fast PostgreSQL-based search on asset metadata

### Technical Highlights
- **Authentication**: OpenID Connect (OIDC) via Keycloak
- **Authorization**: Claims-based with role/group mapping
- **Async Processing**: Hangfire background jobs for media processing
- **Object Storage**: S3-compatible MinIO for scalable file storage
- **Presigned URLs**: Direct browser download without proxying through API
- **Docker Compose**: Complete local development environment

## 🛠️ Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| API | ASP.NET Core 9 (Minimal APIs) | .NET 9 |
| Frontend | Blazor Server + MudBlazor | - |
| Auth | Keycloak | 24.0.1 |
| Database | PostgreSQL | 16-alpine |
| Storage | MinIO | Latest |
| Background Jobs | Hangfire | With PostgreSQL storage |
| Media Tools | ImageMagick, ffmpeg | Docker images |
| Deployment | Docker Compose | Latest |

## 📋 Prerequisites

- **Docker Desktop** (Windows/Mac) or Docker Engine + Docker Compose (Linux)
- **Git**
- **.NET 9 SDK** (optional, only if running without Docker)
- **Ports available**: 7252 (API), 8080 (Keycloak), 5432 (PostgreSQL), 9000/9001 (MinIO)
- **Hosts file entry**: Add `127.0.0.1 keycloak` to your hosts file (see Quick Start)

## 🚀 Quick Start

### 1. Add Keycloak to Hosts File
Add this line to your hosts file:
- **Windows**: `C:\Windows\System32\drivers\etc\hosts`
- **Mac/Linux**: `/etc/hosts`

```
127.0.0.1 keycloak
```

This ensures JWT tokens issued by Keycloak have the correct issuer URL.

### 2. Clone and Navigate
```bash
git clone <repository-url>
cd AssetHub
```

### 3. Start All Services
```bash
docker compose up --build
```

This will start:
- **PostgreSQL 16** on `localhost:5432`
- **MinIO** on `localhost:9000` (API) and `localhost:9001` (Console)
- **Keycloak** on `keycloak:8080` (requires hosts file entry)
- **API** on `localhost:7252`
- **Hangfire Worker** (background job processing)

### 4. Access the Application
- **App**: http://localhost:7252
- **Keycloak Admin**: http://keycloak:8080/admin (admin / admin123)
- **MinIO Console**: http://localhost:9001

### 5. Log In
Use one of the test users:
- **Username**: `testuser` / **Password**: `testuser123` (viewer)
- **Username**: `mediaadmin` / **Password**: `mediaadmin123` (admin)

## 📚 API Endpoints

### Collections (Active)
```
GET    /api/collections              # List root collections
GET    /api/collections/{id}         # Get collection details
POST   /api/collections              # Create collection
PATCH  /api/collections/{id}         # Update collection
DELETE /api/collections/{id}         # Delete collection
GET    /api/collections/{id}/children
POST   /api/collections/{id}/acl     # Assign roles
GET    /api/collections/{id}/acl     # Get ACLs
POST   /api/collections/{id}/acl/revoke
```

### Assets (Active)
```
GET    /api/assets                   # List assets
GET    /api/assets/{id}              # Get asset details
POST   /api/assets/upload            # Upload asset (multipart form)
PATCH  /api/assets/{id}              # Update asset metadata
DELETE /api/assets/{id}              # Delete asset
```

### Shares (Active)
```
POST   /api/shares                   # Create share token (auth required)
GET    /api/shares/{token}           # Get shared asset/collection (public)
GET    /api/shares/{token}/download  # Download via share link (public, redirects to MinIO)
DELETE /api/shares/{id}              # Revoke share (auth required)
```

## 🔑 Authentication & Security

### Current Status
- ✅ OIDC authentication working via Keycloak
- ✅ JWT Bearer token validation for API endpoints
- ✅ Cookie authentication for Blazor Server UI
- ✅ Claims-based authorization
- ✅ Dual auth scheme (Cookie + JWT Bearer)

### How It Works
1. User logs in via browser → Keycloak OIDC flow
2. Keycloak returns JWT token to browser
3. Browser sends token in `Authorization: Bearer <token>` header
4. API validates token signature against Keycloak's public key
5. Claims (user ID, roles, groups) extracted and available in `HttpContext.User`

## 📊 Project Status

### Phase 1: Foundation ✅ COMPLETE
- [x] Docker Compose with all services
- [x] PostgreSQL migrations
- [x] Collections API (10 endpoints, fully functional)
- [x] Keycloak OIDC authentication

### Phase 2: Media Processing ✅ TESTED
- [x] Asset endpoints implemented and tested
- [x] Image processing with ImageMagick (thumbnail + medium generation)
- [x] Hangfire job scheduling (verified working)
- [x] Asset upload flow fully tested
- [x] Share link creation, retrieval, and download working
- [x] Public share endpoints with AllowAnonymous

### Phase 3: UI & Features ❌ NOT STARTED
- [ ] Blazor UI components
- [ ] Asset grid with search
- [ ] Share management UI
- [ ] Unit & integration tests

See [IMPLEMENTATION_PLAN.md](instructions%20and%20docs/IMPLEMENTATION_PLAN.md) for detailed status and next steps.

## 🔐 Credentials & Configuration

See [CREDENTIALS.md](instructions%20and%20docs/CREDENTIALS.md) for:
- Keycloak admin credentials
- Test user accounts
- MinIO access keys
- Connection strings
- Troubleshooting guide

## 🛠️ Development

### Project Structure
```
d:\projects\AssetHub/
├── Program.cs                    # Application configuration
├── Components/                   # Blazor components & pages
├── Properties/                   # Launch settings
├── appsettings.json             # Base configuration
├── appsettings.Development.json # Dev overrides
├── AssetHub.csproj              # Project file
├── AssetHub.sln                 # Solution file
├── docker-compose.yml           # Multi-service orchestration
└── instructions and docs/
    ├── IMPLEMENTATION_PLAN.md    # Detailed phase breakdown
    └── CREDENTIALS.md            # Credentials & troubleshooting
```

### Building Locally (without Docker)
```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run (requires PostgreSQL, MinIO, Keycloak running)
dotnet run --project AssetHub.csproj
```

### Common Commands

#### View Logs
```bash
# API logs
docker logs assethub-api

# Keycloak logs
docker logs keycloak

# Worker logs
docker logs assethub-worker

# All services
docker compose logs -f
```

#### Database Commands
```bash
# Access PostgreSQL
docker exec -it assethub-postgres psql -U postgres -d assethub

# Run migrations
docker exec assethub-api dotnet ef database update
```

#### MinIO Console
Visit http://localhost:9001
- Access Key: `minioadmin`
- Secret Key: `minioadmin_dev_password`

## 🔍 Monitoring

### Hangfire Dashboard
Visit http://localhost:7252/hangfire to monitor background jobs:
- Queued jobs
- Processing jobs
- Failed jobs
- Job history

### Keycloak Admin Console
Visit http://keycloak:8080/admin (admin / admin123) to:
- Manage users
- Create/modify realms
- Configure clients
- Assign roles

## 🚨 Troubleshooting

### API won't start
```bash
# Check logs
docker logs assethub-api

# Verify PostgreSQL is running
docker exec assethub-postgres pg_isready

# Rebuild and restart
docker compose down
docker compose up --build
```

### Can't log in
- Verify Keycloak is running: `docker exec keycloak /opt/keycloak/bin/healthcheck.sh`
- Check test user exists in Keycloak admin console
- Verify `media` realm is created
- Check browser console for OIDC errors

### MinIO access issues
- Verify bucket exists: `docker exec assethub-minio mc ls minio`
- Check credentials in appsettings.json

See [CREDENTIALS.md](instructions%20and%20docs/CREDENTIALS.md) for detailed troubleshooting.

## 📈 Scaling & Deployment

### Docker Compose Scaling
```bash
# Scale worker to 3 instances
docker compose up -d --scale assethub-worker=3
```

### Environment Configuration
- **Development**: Current docker-compose.yml (local volumes, no persistence)
- **Production**: See IMPLEMENTATION_PLAN.md Phase 3D for production configuration

### Database Backup
```bash
# Backup PostgreSQL
docker exec assethub-postgres pg_dump -U postgres assethub > backup.sql

# Restore
docker exec -i assethub-postgres psql -U postgres assethub < backup.sql
```

## 🤝 Contributing

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Make changes and commit: `git commit -am 'Add feature'`
3. Push to branch: `git push origin feature/your-feature`
4. Open a Pull Request

### Code Style
- C# naming conventions: PascalCase for public members
- Follow existing patterns in CollectionEndpoints.cs for new endpoints
- Add `[FromServices]` attribute to dependency-injected parameters

## 📝 License

MIT License - See LICENSE file for details

## 🗺️ Roadmap

### Completed (January 2026)
- [x] Phase 1: Foundation & Collections
- [x] Phase 2A: Asset Upload & Processing (tested)
- [x] Phase 2B: Share Links (fully implemented)
- [x] JWT Bearer authentication for API
- [x] ImageMagick thumbnail generation

### In Progress
- [ ] Phase 3: Blazor UI & Grid
- [ ] Phase 3: Testing & Hardening
- [ ] Phase 3: Production Deployment
- [ ] Full-text search refinement
- [ ] Advanced media processing (HLS streaming, transcoding)
- [ ] Mobile responsiveness
- [ ] Group management UI

## 📞 Support

For issues or questions:
1. Check [CREDENTIALS.md](instructions%20and%20docs/CREDENTIALS.md) troubleshooting section
2. Review [IMPLEMENTATION_PLAN.md](instructions%20and%20docs/IMPLEMENTATION_PLAN.md) for architecture details
3. Check Docker Compose logs: `docker compose logs -f`
4. Review API response status codes and error messages

---

**Last Updated**: January 27, 2026  
**Status**: Phase 1 Complete ✅ | Phase 2 Tested ✅ | Phase 3 Not Started ❌
