# Microservice Migration Strategy Analysis

## Executive Summary

AssetHub is a well-structured Clean Architecture monolith with strict layer dependencies, 27+ service interfaces following ISP, and a separate Worker container. This analysis evaluates a potential migration to microservices, identifies bounded contexts, proposes a phased strategy, and highlights the trade-offs involved.

**Key finding:** The existing architecture already provides most of the modularity benefits of microservices. A migration should only be pursued if the team faces concrete scaling, deployment, or team-autonomy problems that the current architecture cannot solve.

---

## Table of Contents

- [Current Architecture Assessment](#current-architecture-assessment)
- [Migration Readiness Scorecard](#migration-readiness-scorecard)
- [Bounded Context Identification](#bounded-context-identification)
- [Target Architecture](#target-architecture)
- [Migration Phases](#migration-phases)
  - [Phase 0: Preparatory Refactoring (Monolith)](#phase-0-preparatory-refactoring-monolith)
  - [Phase 1: Strangler Fig — Extract Media Processing](#phase-1-strangler-fig--extract-media-processing)
  - [Phase 2: Extract Sharing Service](#phase-2-extract-sharing-service)
  - [Phase 3: Extract Audit & Analytics](#phase-3-extract-audit--analytics)
  - [Phase 4: Extract Collection Management](#phase-4-extract-collection-management)
  - [Phase 5: Final Decomposition — Asset Core](#phase-5-final-decomposition--asset-core)
- [Cross-cutting Concerns](#cross-cutting-concerns)
- [Data Migration Strategy](#data-migration-strategy)
- [Infrastructure Requirements](#infrastructure-requirements)
- [Risk Assessment](#risk-assessment)
- [Decision Framework: Should You Migrate?](#decision-framework-should-you-migrate)

---

## Current Architecture Assessment

### Strengths (Migration-Friendly)

| Aspect | Current State | Migration Impact |
|--------|--------------|------------------|
| **Layer separation** | Strict Clean Architecture (Domain → Application → Infrastructure → API/Worker) | Boundaries are clear; extraction targets are well-defined |
| **Interface segregation** | 27+ fine-grained service interfaces | Each interface is a natural API contract candidate |
| **Worker isolation** | Separate container with own Dockerfile | Media processing is already containerized; minimal extraction effort |
| **Error handling** | `ServiceResult<T>` monadic pattern | Maps cleanly to HTTP status codes / gRPC status |
| **Auth abstraction** | OIDC/JWT via Keycloak, `CurrentUser` scoped | Token-based auth works natively across services |
| **Storage abstraction** | `IMinIOAdapter` behind interface | Shared storage layer easily becomes a service or stays shared |
| **Resilience** | Polly pipelines on all external calls | Pattern transfers directly to inter-service communication |
| **Observability** | OpenTelemetry traces + metrics already in place | Distributed tracing requires minimal additional effort |
| **API versioning** | `/api/v1/` prefix | Stable contracts reduce migration risk |

### Weaknesses (Migration Obstacles)

| Aspect | Current State | Migration Cost |
|--------|--------------|----------------|
| **Single database** | All 8 entities in one `AssetHubDbContext`, one PostgreSQL instance | **HIGH** — Database decomposition is the hardest part |
| **PostgreSQL coupling** | JSONB, `text[]`, `pg_trgm`, GIN indexes, `ILike()` | Each service must retain PostgreSQL or rewrite queries |
| **Shared DI root** | `AddSharedInfrastructure()` registers everything for both API and Worker | Must split into per-service registrations |
| ~~**Hangfire coupling**~~ | ~~Shared job storage in PostgreSQL, both hosts process same queues~~ | ✅ **RESOLVED** — Migrated to Wolverine + RabbitMQ message bus |
| **Cross-entity queries** | Dashboard aggregates across Assets, Collections, Shares, Audit | Distributed queries require new patterns (API composition, CQRS) |
| **Collection authorization** | ACL checks require joining Collections, CollectionAcls, AssetCollections | Authorization context must be replicated or centralized |
| **Blazor Server UI** | Server-rendered with SignalR circuits, `AssetHubApiClient` calls local API | Must become a BFF (Backend for Frontend) or switch to Blazor WASM |

---

## Migration Readiness Scorecard

| Criterion | Score (1-5) | Notes |
|-----------|:-----------:|-------|
| Domain boundary clarity | 4 | ISP interfaces define clear functional areas |
| Data coupling | 2 | Single shared database with cross-entity JOINs |
| Team structure readiness | ? | Requires ≥3-4 teams to justify the operational overhead |
| Deployment pain | ? | Currently 2 containers; microservices would be 6-8+ |
| Scaling bottlenecks | 3 | Media processing scales independently (Worker), but DB is shared |
| API maturity | 4 | Versioned, well-structured Minimal API endpoints |
| Testing coverage | 4 | Integration + unit + E2E; Testcontainers for DB |
| Observability | 4 | OpenTelemetry, structured logging, health checks |
| **Overall** | **~3.0** | Ready for selective extraction, not a full decomposition |

---

## Bounded Context Identification

Based on domain analysis, service interface cohesion, and data ownership patterns:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        IDENTIFIED BOUNDED CONTEXTS                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────┐  │
│  │  ASSET CORE      │  │  COLLECTION MGT  │  │  SHARING             │  │
│  │                  │  │                  │  │                      │  │
│  │  • Asset CRUD    │  │  • Collection    │  │  • Share CRUD        │  │
│  │  • Upload        │  │    CRUD          │  │  • Token validation  │  │
│  │  • Renditions    │  │  • ACL mgmt      │  │  • Password auth     │  │
│  │  • Search        │  │  • Authorization │  │  • Anonymous access  │  │
│  │  • Deletion      │  │  • Membership    │  │  • Rate limiting     │  │
│  │                  │  │                  │  │                      │  │
│  │  Entities:       │  │  Entities:       │  │  Entities:           │  │
│  │  Asset           │  │  Collection      │  │  Share               │  │
│  │                  │  │  CollectionAcl   │  │                      │  │
│  │                  │  │  AssetCollection │  │                      │  │
│  └──────────────────┘  └──────────────────┘  └──────────────────────┘  │
│                                                                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────┐  │
│  │  MEDIA PROC.     │  │  IDENTITY        │  │  AUDIT & ANALYTICS   │  │
│  │                  │  │                  │  │                      │  │
│  │  • Image proc.   │  │  • Keycloak sync │  │  • Event recording   │  │
│  │  • Video proc.   │  │  • User CRUD     │  │  • Retention         │  │
│  │  • Metadata      │  │  • Provisioning  │  │  • Dashboard queries │  │
│  │    extraction    │  │  • Cleanup       │  │  • Analytics          │  │
│  │  • Zip building  │  │  • Role mapping  │  │                      │  │
│  │                  │  │                  │  │  Entities:           │  │
│  │  Entities:       │  │  Entities: none  │  │  AuditEvent          │  │
│  │  ZipDownload     │  │  (Keycloak-owned)│  │  (Dashboard = view)  │  │
│  └──────────────────┘  └──────────────────┘  └──────────────────────┘  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Context Coupling Map

```
Asset Core ──── strong ────► Collection Mgt    (assets belong to collections)
Asset Core ──── strong ────► Media Processing  (upload triggers processing)
Asset Core ──── weak ──────► Sharing           (shares reference assets by ID)
Asset Core ──── weak ──────► Audit             (asset events logged)
Collection Mgt ── weak ────► Sharing           (shares reference collections by ID)
Collection Mgt ── weak ────► Audit             (collection events logged)
Identity ──────── weak ────► Collection Mgt    (provisioning grants default ACLs)
Identity ──────── weak ────► Audit             (user events logged)
Sharing ────────── weak ───► Audit             (share events logged)
```

**Strong coupling** = synchronous calls required, shared data
**Weak coupling** = event-driven or ID-based references, no shared data

---

## Target Architecture

```
                         ┌───────────────────────────┐
                         │      API GATEWAY          │
                         │  (YARP / Ocelot / Envoy)  │
                         │  • Route-based dispatch    │
                         │  • JWT validation          │
                         │  • Rate limiting           │
                         │  • Request aggregation     │
                         └─────┬───────┬────┬────────┘
                               │       │    │
              ┌────────────────┘       │    └────────────────┐
              ▼                        ▼                     ▼
   ┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
   │  Asset Service    │   │  Collection Svc  │   │  Sharing Service │
   │  /api/v1/assets   │   │  /api/v1/coll.   │   │  /api/v1/shares  │
   │  ┌──────────────┐ │   │  ┌──────────────┐│   │  ┌──────────────┐│
   │  │ PostgreSQL   │ │   │  │ PostgreSQL   ││   │  │ PostgreSQL   ││
   │  │ (assets)     │ │   │  │ (collections ││   │  │ (shares)     ││
   │  │              │ │   │  │  + ACLs)     ││   │  │              ││
   │  └──────────────┘ │   │  └──────────────┘│   │  └──────────────┘│
   └────────┬──────────┘   └────────┬─────────┘   └────────┬─────────┘
            │                       │                       │
            ▼                       ▼                       ▼
   ┌──────────────────────────────────────────────────────────────────┐
   │                     MESSAGE BROKER (RabbitMQ)                    │
   │  Exchanges: asset.events, collection.events, share.events       │
   └──────────┬──────────────────┬──────────────────────┬────────────┘
              │                  │                      │
              ▼                  ▼                      ▼
   ┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
   │  Media Processor  │   │  Audit Service   │   │  Identity Svc    │
   │  (Worker)         │   │  /api/v1/audit   │   │  (Keycloak +     │
   │  • ImageMagick    │   │  /api/v1/dashboard│   │   sync worker)   │
   │  • ffmpeg         │   │  ┌──────────────┐│   │                  │
   │  • Zip builder    │   │  │ PostgreSQL   ││   │  • User CRUD     │
   │                   │   │  │ (audit_events││   │  • Provisioning  │
   │                   │   │  │  + analytics)││   │  • Cleanup        │
   │                   │   │  └──────────────┘│   │                  │
   └───────────────────┘   └──────────────────┘   └──────────────────┘
              │
              ▼
   ┌──────────────────┐
   │  MinIO (Shared)   │
   │  S3-compatible    │
   │  object storage   │
   └──────────────────┘


   ┌──────────────────────────────────────────────────────────────────┐
   │  SHARED INFRASTRUCTURE                                           │
   │  • Redis (caching, SignalR backplane)                            │
   │  • Keycloak (OIDC provider — external to all services)          │
   │  • MinIO (object storage — shared, not decomposed)              │
   │  • Aspire Dashboard / Grafana (observability)                    │
   └──────────────────────────────────────────────────────────────────┘
```

---

## Migration Phases

### Phase 0: Preparatory Refactoring (Monolith)

**Goal:** Reduce coupling without changing the deployment model. This is the most important phase.

**Duration estimate:** 4-6 weeks

#### 0.1 — Introduce Domain Events

Replace direct cross-context calls with in-process domain events using MediatR:

```
Current (direct coupling):
  AssetUploadService → AuditService.LogAsync(...)
  AssetUploadService → MediaProcessingService.ScheduleAsync(...)
  UserAdminService → CollectionAclService.GrantDefaultAccess(...)

Target (event-driven):
  AssetUploadService → publish AssetUploadedEvent
  → Handler 1: AuditEventHandler logs the event
  → Handler 2: MediaProcessingHandler schedules processing
  → Handler 3: (future) NotificationHandler sends email
```

**Files to modify:** All services that call across bounded context boundaries (~12 service implementations).

#### 0.2 — Split the Database Context

Break `AssetHubDbContext` into context-aligned DbContexts that still target the same database:

```csharp
// Before: 1 context, 8 DbSets
AssetHubDbContext { Assets, Collections, CollectionAcls, AssetCollections, Shares, AuditEvents, ZipDownloads, DataProtectionKeys }

// After: 4 focused contexts, same database
AssetDbContext        { Assets }
CollectionDbContext   { Collections, CollectionAcls, AssetCollections }
ShareDbContext        { Shares }
AuditDbContext        { AuditEvents }
// ZipDownloads stays with AssetDbContext (tightly coupled to asset downloads)
// DataProtectionKeys stays with a shared context
```

Cross-context queries (like Dashboard) use read-only projections via raw SQL or a dedicated query context.

#### ~~0.3 — Replace Hangfire with Message Broker~~ ✅ COMPLETED

~~Introduce RabbitMQ (or Azure Service Bus) alongside Hangfire:~~

This step has been completed. The application now uses **Wolverine + RabbitMQ** for all message-driven processing:
- `ProcessImageCommand` / `ProcessVideoCommand` / `BuildZipCommand` — published to RabbitMQ queues
- `AssetProcessingCompletedEvent` / `AssetProcessingFailedEvent` — consumed back by the API
- Recurring maintenance tasks run as `IHostedService` classes in the Worker (no Hangfire dependency)
- Hangfire PostgreSQL storage dependency has been fully removed

#### 0.4 — Extract Shared Libraries

Create NuGet packages (or project references) for cross-cutting code:

```
AssetHub.Contracts/        — DTOs, events, commands (shared by all services)
AssetHub.Auth/             — CurrentUser, authorization policies, JWT parsing
AssetHub.Infrastructure.Common/ — ServiceResult, Polly pipelines, MinIO adapter
```

---

### Phase 1: Strangler Fig — Extract Media Processing

**Priority: FIRST** — Lowest risk, already a separate container.

**Current state:** `AssetHub.Worker` is a separate Dockerfile running Wolverine message consumers via RabbitMQ.

**Target state:** Independent service consuming messages from RabbitMQ.

#### Steps

1. **Create `AssetHub.MediaProcessor` service** (new project)
   - Consumes `ImageProcessingCommand` and `VideoProcessingCommand` from RabbitMQ
   - Publishes `AssetProcessingCompletedEvent` / `AssetProcessingFailedEvent`
   - Owns `ZipDownload` entity in its own database schema
   - References MinIO directly (shared storage)

2. **API service subscribes to completion events**
   - `AssetProcessingCompletedEvent` → update Asset status to Ready, set rendition keys
   - `AssetProcessingFailedEvent` → update Asset status to Failed

3. **Decommission legacy media job calls**
   - Remove any remaining direct processing calls
   - Replace with Wolverine `Publish<ImageProcessingCommand>()`
   - Replace with MassTransit `Publish<ImageProcessingCommand>()`

4. **Validation**
   - Upload an image → verify processing completes via event
   - Upload a video → verify poster extraction
   - Request a zip download → verify async build

#### Data Ownership

| Entity | Owner | Access Pattern |
|--------|-------|---------------|
| ZipDownload | Media Processor | Full CRUD |
| Asset (status) | Asset Core | Media Processor publishes events; Asset Core updates |
| MinIO objects | Shared | Both services read/write directly |

---

### Phase 2: Extract Sharing Service

**Priority: SECOND** — Well-isolated, polymorphic scope already decoupled from FKs.

#### Steps

1. **Create `AssetHub.Sharing` service**
   - Owns: `Share` entity in its own database
   - Endpoints: all `/api/v1/shares/*` routes
   - Publishes: `ShareCreatedEvent`, `ShareRevokedEvent`, `ShareAccessedEvent`

2. **API Gateway routes** `/api/v1/shares/*` to the new service

3. **Asset/Collection lookups** — When validating a share's scope:
   - Share service calls Asset Service or Collection Service via HTTP to verify the target exists
   - Cache scope validation results (scope targets rarely change)

4. **Anonymous share content delivery** — The Sharing service generates presigned MinIO URLs directly (it already has the object keys from the share's scope)

#### Key Challenge

The current share access flow loads asset/collection data to build the response. In a decomposed world, the Sharing service must:
- Store denormalized scope metadata (title, thumbnail key) at share creation time, OR
- Call the Asset/Collection service at access time (adds latency)

**Recommendation:** Denormalize at creation time + subscribe to update events to keep metadata fresh.

---

### Phase 3: Extract Audit & Analytics

**Priority: THIRD** — Write-heavy, append-only, perfect for event sourcing.

#### Steps

1. **Create `AssetHub.Audit` service**
   - Owns: `AuditEvent` entity in its own database
   - Consumes: all `*Event` messages from the broker (asset, collection, share, user events)
   - Endpoints: `/api/v1/admin/audit/*`, `/api/v1/dashboard`
   - Runs: retention cleanup job internally

2. **Replace direct `IAuditService` calls** with event publication
   - Every service publishes domain events
   - Audit service subscribes and records them

3. **Dashboard queries** — The Audit service maintains materialized aggregations:
   - Asset count, collection count, share count → updated via event counters
   - Recent activity → sourced from audit events
   - Storage usage → periodic poll of MinIO or event-driven accumulator

#### Benefits

- Audit writes no longer compete with transactional workload
- Retention policies can be managed independently
- Analytics can be scaled separately (read-heavy dashboard queries)

---

### Phase 4: Extract Collection Management

**Priority: FOURTH** — Tightly coupled to assets via the many-to-many relationship.

#### Steps

1. **Create `AssetHub.Collections` service**
   - Owns: `Collection`, `CollectionAcl`, `AssetCollection` entities
   - Endpoints: `/api/v1/collections/*`
   - Publishes: `CollectionCreatedEvent`, `AclChangedEvent`, `AssetAddedToCollectionEvent`

2. **Authorization as a service endpoint**
   - Expose `GET /internal/collections/{id}/authorize?userId={}&requiredRole={}` for other services
   - Asset service calls this before asset operations within a collection
   - Cache authorization results with short TTL (30s) in Redis

3. **Asset-Collection membership**
   - Collection service owns the `AssetCollection` join table
   - Asset service publishes `AssetDeletedEvent` → Collection service removes memberships
   - Collection service publishes `AssetRemovedFromCollectionEvent` → Asset service can react

#### Key Challenge

The current asset listing endpoints filter by collection. In a decomposed world:
1. Client requests assets in collection X
2. API Gateway routes to Collection Service → returns list of asset IDs
3. Asset Service fetches asset details for those IDs
4. API Gateway or BFF composes the response

This is the **API Composition** pattern and adds latency. Mitigation: maintain a denormalized asset-list cache in the Collection service.

---

### Phase 5: Final Decomposition — Asset Core

**Priority: LAST** — This is the core domain; extract only if team/scaling needs justify it.

#### What Remains

After phases 1-4, the monolith contains:
- Asset CRUD and metadata
- Upload pipeline (validation, malware scan, storage)
- Search and rendition delivery
- The Blazor UI (BFF pattern)

This becomes `AssetHub.Assets` — the core service. The Blazor UI either:
- **Option A:** Stays as a BFF in the same container, calling other services via HTTP
- **Option B:** Migrates to Blazor WebAssembly, calling all services through the API Gateway

**Recommendation:** Option A — keep Blazor Server as BFF. It already uses `AssetHubApiClient` for HTTP calls; just point it at the gateway instead of localhost.

---

## Cross-cutting Concerns

### Authentication & Authorization

```
┌──────────────────────────────────────────────────────┐
│  Every service validates JWT tokens independently     │
│  using Keycloak's OIDC discovery endpoint             │
│                                                       │
│  Token contains: user_id, username, realm_roles        │
│  Per-collection ACLs: call Collection Service          │
│  Share tokens: validated by Sharing Service            │
└──────────────────────────────────────────────────────┘
```

- **JWT validation** is stateless — every service can validate tokens independently using Keycloak's public key (JWKS endpoint)
- **Collection ACLs** become an internal API on the Collection Service
- **Rate limiting** moves to the API Gateway (global) + per-service (specific)

### Distributed Tracing

Already using OpenTelemetry. For microservices:
- Propagate `traceparent` header across HTTP calls and message broker
- MassTransit has built-in OpenTelemetry integration
- Each service exports to the same OTLP collector

### Shared Libraries (NuGet Packages)

```
AssetHub.Contracts         — Events, commands, shared DTOs, enums
AssetHub.Auth.Common       — JWT parsing, CurrentUser, role hierarchy
AssetHub.Infrastructure.Core — ServiceResult<T>, MinIO adapter, Polly pipeline builders
AssetHub.Testing.Common    — TestAuthHandler, shared test fixtures
```

### Service Communication Patterns

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Synchronous HTTP** | Query that needs immediate response | Asset Service → Collection Service for ACL check |
| **Async messaging** | Fire-and-forget commands | Upload → Media Processing command |
| **Event publishing** | Notify interested parties | `AssetDeletedEvent` → Audit, Collection, Sharing |
| **API Composition** | Aggregate data from multiple services | Dashboard = counts from all services |

---

## Data Migration Strategy

### Approach: Database-per-Service via Schema Split

**Step 1: Logical split** (Phase 0)
- Create separate schemas within the same PostgreSQL instance
- `asset_schema.assets`, `collection_schema.collections`, `share_schema.shares`, `audit_schema.audit_events`
- Cross-schema views for transition period

**Step 2: Physical split** (Phases 1-4)
- Each new service gets its own PostgreSQL instance
- Use Change Data Capture (CDC) via Debezium or PostgreSQL logical replication for the transition
- Dual-write during migration window, then cut over

### Data Denormalization Requirements

| Service | Denormalized Data | Source | Sync Mechanism |
|---------|------------------|--------|----------------|
| Sharing | Asset title, thumbnail key | Asset Service | `AssetUpdatedEvent` |
| Collection | Asset count per collection | Asset Service | `AssetAddedToCollectionEvent` / `AssetDeletedEvent` |
| Audit | Username, email | Identity Service | `UserUpdatedEvent` |
| Dashboard | All aggregate counts | All services | Event-driven counters |

### Migration Safety

- **Zero-downtime requirement:** Use expand-contract pattern for schema changes
- **Rollback strategy:** Keep the monolith database as primary until the new service is proven
- **Data consistency:** Accept eventual consistency (seconds, not minutes) between services
- **Idempotent consumers:** All message handlers must be idempotent (use message deduplication)

---

## Infrastructure Requirements

### New Components Needed

| Component | Purpose | Options |
|-----------|---------|---------|
| **API Gateway** | Routing, auth, rate limiting | YARP (.NET), Envoy, Kong, Ocelot |
| **Message Broker** | Async communication | RabbitMQ (recommended — MassTransit native), Azure Service Bus, Kafka |
| **Service Discovery** | Dynamic service location | Consul, Kubernetes DNS, DNS-based (Docker Compose) |
| **Container Orchestration** | Service management | Kubernetes, Docker Swarm, Azure Container Apps |
| **Centralized Logging** | Log aggregation | ELK stack, Grafana Loki, Azure Monitor |
| **Distributed Cache** | Cross-service caching | Redis (already in place) |
| **Schema Registry** | Contract versioning | NuGet packages (simpler), Confluent Schema Registry (Kafka) |

### Resource Estimates (vs. Current)

| Metric | Current (Monolith) | Target (Microservices) | Delta |
|--------|-------------------|----------------------|-------|
| Containers | 9 (api, worker, postgres, minio, keycloak, redis, mailpit, clamav, aspire) | 14-16 (+gateway, +broker, +4 service instances) | +5-7 |
| PostgreSQL instances | 1 | 4-5 (one per service with own data) | +3-4 |
| Memory footprint | ~2 GB (api + worker) | ~4-6 GB (all services) | +2-4 GB |
| Operational complexity | Low (2 app containers) | High (8+ app containers, broker, gateway) | Significant |

---

## Risk Assessment

### High Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Distributed transactions** | Upload pipeline spans storage + DB + processing | Use saga pattern with compensating actions; accept eventual consistency |
| **Collection authorization latency** | Every asset operation needs an ACL check | Cache authorization results in Redis with short TTL; use sidecar or embedded library |
| **Dashboard data staleness** | Aggregate queries span all services | Event-driven materialized views; accept seconds of staleness |
| **Operational overhead** | 4x more services to deploy, monitor, debug | Invest in observability, CI/CD automation, and runbooks before migration |
| **Team size mismatch** | Microservices need ≥1 team per 2-3 services | Do not migrate if team is <4 engineers |

### Medium Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Message ordering** | Events processed out of order | Use message ordering keys (per-entity), idempotent handlers |
| **Network partitions** | Service-to-service calls fail | Circuit breakers (Polly already in use), fallback responses |
| **Schema evolution** | Shared contracts change | Versioned NuGet packages, backwards-compatible event schemas |
| **Testing complexity** | Integration tests span services | Contract testing (Pact), service virtualization, consumer-driven contracts |

### Low Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| **MinIO shared access** | Multiple services access same buckets | Namespace object keys by service, or use bucket-per-service |
| **Keycloak dependency** | Single OIDC provider | Already external; no change needed |

---

## Decision Framework: Should You Migrate?

### Migrate IF:

- [ ] **Multiple teams** (3+) need to deploy independently on different cadences
- [ ] **Scaling bottleneck** exists that cannot be solved by scaling the Worker or adding read replicas
- [ ] **Technology heterogeneity** is needed (e.g., media processing in Go/Rust for performance)
- [ ] **Fault isolation** is critical (one module's crash should not take down others)
- [ ] **Compliance requirements** demand data isolation (e.g., audit data in a separate jurisdiction)

### Do NOT migrate IF:

- [ ] The team is **<4 engineers** (operational overhead will overwhelm productivity)
- [ ] The primary goal is just **"better code organization"** (refactor the monolith instead)
- [ ] **Deployment velocity** is not a bottleneck (you can deploy the monolith in minutes)
- [ ] You want microservices because they are **"industry best practice"** (they are not universal best practice)

### Recommended Middle Ground: Modular Monolith

If the decision matrix is mixed, consider stopping at Phase 0:

1. Split the DbContext into bounded contexts
2. Introduce domain events via MediatR
3. Extract shared libraries
4. Keep deploying as 2 containers (API + Worker)

This gives 80% of the organizational benefits at 20% of the operational cost. You can always extract services later when a concrete need arises — the Phase 0 refactoring makes future extraction straightforward.

---

## Summary

| Phase | Effort | Risk | Value |
|-------|--------|------|-------|
| **Phase 0: Refactor monolith** | Medium | Low | High — enables everything else |
| **Phase 1: Media Processing** | Low | Low | Medium — already isolated |
| **Phase 2: Sharing** | Medium | Medium | Medium — well-bounded |
| **Phase 3: Audit** | Medium | Low | High — removes write contention |
| **Phase 4: Collections** | High | High | Medium — tightly coupled to assets |
| **Phase 5: Asset Core** | High | Medium | Low — remaining monolith is fine |

**The strongest recommendation is to complete Phase 0 and Phase 1, then reassess.** The modular monolith approach provides the best return on investment for most teams, with a clear path to further decomposition when the need is proven.
