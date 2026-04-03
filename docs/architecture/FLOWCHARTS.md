# AssetHub — Major Function Flowcharts

This document describes the major workflows in AssetHub using Mermaid flowcharts.

---

## Table of Contents

1. [Request Pipeline](#1-request-pipeline)
2. [Authentication & Authorization](#2-authentication--authorization)
3. [Asset Upload](#3-asset-upload)
4. [Asset Query & Retrieval](#4-asset-query--retrieval)
5. [Collection Management](#5-collection-management)
6. [Share & Public Access](#6-share--public-access)
7. [Background Job Processing](#7-background-job-processing)

---

## 1. Request Pipeline

How an incoming HTTP request flows through the ASP.NET Core middleware pipeline to reach an endpoint handler and produce a response.

```mermaid
flowchart TD
    A[Incoming HTTP Request] --> B[ForwardedHeaders]
    B --> G{Production?}
    G -->|Yes| H[HTTPS Redirect + HSTS]
    G -->|No| I[Skip HTTPS]
    H --> J[Security Headers]
    I --> J
    J --> K[Global Exception Handler]
    K --> L[Static Files]
    L --> M[Serilog Request Logging]
    M --> N[Request Localization]
    N --> O[Rate Limiter]
    O --> P{Rate limit exceeded?}
    P -->|Yes| Q[429 Too Many Requests]
    P -->|No| R[Authentication]
    R --> S[Authorization]
    S --> T[Antiforgery]
    T --> U{Route matched?}
    U -->|API endpoint| V[ValidationFilter]
    U -->|Blazor page| W[Blazor Server]
    U -->|No match| X[404 Not Found]
    V --> Y{DTO valid?}
    Y -->|No| Z[400 Bad Request]
    Y -->|Yes| AA[Endpoint Handler]
    AA --> AB[Service Layer]
    AB --> AC{ServiceResult}
    AC -->|Success, no value| AD[204 No Content]
    AC -->|Success, with value| AE[200 OK / 201 Created]
    AC -->|NotFound| AF[404 Not Found]
    AC -->|Forbidden| AG[403 Forbid]
    AC -->|BadRequest| AH[400 Bad Request]
    AC -->|Conflict| AI[409 Conflict]
    AC -->|Server| AJ[500 Internal Error]

    style E fill:#f66,color:#fff
    style Q fill:#f66,color:#fff
    style Z fill:#f66,color:#fff
    style AF fill:#f66,color:#fff
    style AG fill:#f66,color:#fff
    style AH fill:#f66,color:#fff
    style AI fill:#f66,color:#fff
    style AJ fill:#f66,color:#fff
    style AD fill:#6c6,color:#fff
    style AE fill:#6c6,color:#fff
    style F fill:#6c6,color:#fff
```

---

## 2. Authentication & Authorization

How the PolicyScheme routes between JWT and Cookie/OIDC, and how per-collection RBAC is enforced.

```mermaid
flowchart TD
    A[Incoming Request] --> B{Has Authorization:<br/>Bearer header?}
    B -->|Yes| C[JwtBearer Scheme]
    B -->|No| D[Cookie / OIDC Scheme]

    C --> E{Valid JWT token?}
    E -->|No| F[401 Unauthorized]
    E -->|Yes| G[Extract Claims from JWT]

    D --> H{Has session cookie?}
    H -->|Yes| I[Restore identity from cookie]
    H -->|No| J[Redirect to Keycloak OIDC]
    J --> K[User logs in at Keycloak]
    K --> L[Callback with auth code]
    L --> M[Exchange code for tokens]
    M --> N[Map Keycloak claims]
    N --> O[Create auth cookie]
    O --> I

    G --> P[Build CurrentUser]
    I --> P

    P --> Q{Endpoint requires<br/>authorization?}
    Q -->|No| R[Execute Handler]
    Q -->|Yes| S{Check system role policy}

    S --> T{User role >= required?}
    T -->|No| U[403 Forbidden]
    T -->|Yes| V{Collection-scoped<br/>operation?}

    V -->|No| R
    V -->|Yes| W[Load Collection ACL]

    W --> X{User has ACL entry<br/>for collection?}
    X -->|No| Y{Is system Admin?}
    Y -->|No| U
    Y -->|Yes| R
    X -->|Yes| Z{ACL role >= required?}
    Z -->|No| U
    Z -->|Yes| R

    style F fill:#f66,color:#fff
    style U fill:#f66,color:#fff
    style R fill:#6c6,color:#fff
```

### Role Hierarchy

```mermaid
flowchart LR
    Viewer["Viewer (1)"] --> Contributor["Contributor (2)"]
    Contributor --> Manager["Manager (3)"]
    Manager --> Admin["Admin (4)"]
```

> Higher roles inherit all permissions of lower roles. System Admin bypasses all collection-level checks.

---

## 3. Asset Upload

The complete asset upload pipeline including validation, malware scanning, storage, and background processing.

```mermaid
flowchart TD
    A[Upload Request] --> B{User has Contributor+<br/>role on collection?}
    B -->|No| C[403 Forbidden]
    B -->|Yes| D{File size == 0?}

    D -->|Yes| E[400 Empty file]
    D -->|No| F{Exceeds MaxUploadSizeMb?}

    F -->|Yes| G[400 File too large]
    F -->|No| H{Content-Type in<br/>AllowedUploadTypes?}

    H -->|No| I[400 Unsupported type]
    H -->|Yes| J[Magic Byte Validation]

    J --> K{File bytes match<br/>claimed Content-Type?}
    K -->|No| L[400 Invalid file content]
    K -->|Yes| M[ClamAV Malware Scan]

    M --> N{Scan completed?}
    N -->|No| O[503 Scan unavailable<br/>- fail closed]
    N -->|Yes| P{File is clean?}

    P -->|No| R[400 Malware detected]
    P -->|Yes| S[Create Asset Entity<br/>Status: Processing]

    S --> T[Upload to MinIO<br/>via Polly pipeline]
    T --> U{Upload succeeded?}
    U -->|No| V[503 Storage error]
    U -->|Yes| W[Save to Database]

    W --> X[Add to Collection<br/>AssetCollections join]
    X --> Y[Write Audit Log<br/>asset.created]
    Y --> Z[Publish Wolverine Command<br/>via RabbitMQ]
    Z --> AA[Return 201 Created<br/>with AssetDto]

    style C fill:#f66,color:#fff
    style E fill:#f66,color:#fff
    style G fill:#f66,color:#fff
    style I fill:#f66,color:#fff
    style L fill:#f66,color:#fff
    style O fill:#f66,color:#fff
    style R fill:#f66,color:#fff
    style V fill:#f66,color:#fff
    style AA fill:#6c6,color:#fff
```

---

## 4. Asset Query & Retrieval

How assets are searched, filtered, and returned — with different paths for admins vs regular users.

```mermaid
flowchart TD
    A[Query Request] --> B{Is system Admin?}

    B -->|Yes| C[Admin Path]
    C --> D["Status filter: exclude Uploading only"]
    D --> E[Search ALL assets]

    B -->|No| F[User Path]
    F --> G[Get accessible collections<br/>for user]
    G --> H{Specific collection<br/>requested?}
    H -->|Yes| I{User has access<br/>to collection?}
    I -->|No| J[403 Forbidden]
    I -->|Yes| K[Filter to that collection]
    H -->|No| K2[Use all accessible collections]

    K --> L["Status filter: Ready only"]
    K2 --> L

    L --> M[Join AssetCollections<br/>restrict to allowed IDs]

    E --> N[Apply Search Filters]
    M --> N

    N --> O{Text search?}
    O -->|Yes| P["ILIKE on Title, Description<br/>+ JSONB Tag matching"]
    O -->|No| Q{Asset type filter?}
    P --> Q
    Q -->|Yes| R[WHERE AssetType = type]
    Q -->|No| S[Apply Pagination]
    R --> S

    S --> T[Execute Query]
    T --> U[Batch-load collection mappings<br/>per asset]
    U --> V[Resolve highest role<br/>per asset across collections]
    V --> W[Map to Response DTOs]
    W --> X[Return 200 OK<br/>Paginated Results]

    style J fill:#f66,color:#fff
    style X fill:#6c6,color:#fff
```

### Asset Download / Preview

```mermaid
flowchart TD
    A[Download Request<br/>GET /api/assets/id/download] --> B{User authorized<br/>for asset?}
    B -->|No| C[403 Forbidden]
    B -->|Yes| D{Rendition requested?}
    D -->|thumbnail| E[Look up thumb key]
    D -->|medium| F[Look up medium key]
    D -->|original| G[Use original key]
    D -->|none specified| G

    E --> H{Rendition exists?}
    F --> H
    H -->|No| G
    H -->|Yes| I[Use rendition key]
    G --> I

    I --> J[Generate Presigned URL<br/>MinIO, 30s expiry]
    J --> K[Return URL / Redirect]

    style C fill:#f66,color:#fff
    style K fill:#6c6,color:#fff
```

---

## 5. Collection Management

Creating, updating, deleting collections and managing per-collection access control lists.

```mermaid
flowchart TD
    A[Collection Operation] --> B{Operation type?}

    B -->|Create| C[Create Collection]
    C --> D{Name unique?}
    D -->|No| E[409 Conflict]
    D -->|Yes| F[Save Collection to DB]
    F --> G[Auto-grant creator<br/>Admin role via ACL]
    G --> H[Invalidate Cache]
    H --> I[Return 201 Created]

    B -->|Update| J[Update Collection]
    J --> K{User is Manager+<br/>on collection?}
    K -->|No| L[403 Forbidden]
    K -->|Yes| M[Apply changes<br/>name / description]
    M --> N{New name conflicts?}
    N -->|Yes| E
    N -->|No| O[Save + Invalidate Cache]
    O --> P[Return 200 OK]

    B -->|Delete| Q[Delete Collection]
    Q --> R{User is Admin<br/>on collection?}
    R -->|No| L
    R -->|Yes| S[Delete all assets<br/>in collection from MinIO]
    S --> T[Delete ACLs]
    T --> U[Delete shares]
    U --> V[Delete collection]
    V --> W[Invalidate Cache]
    W --> X[Return 204 No Content]

    B -->|Manage ACL| Y[ACL Operation]
    Y --> Z{User is Manager+<br/>on collection?}
    Z -->|No| L
    Z -->|Yes| AA{ACL action?}
    AA -->|Set Access| AB[Upsert ACL entry<br/>userId + role]
    AA -->|Revoke| AC[Delete ACL entry]
    AA -->|List| AD[Return all ACL entries]
    AB --> AE[Invalidate Cache]
    AC --> AE
    AE --> AF[Return Result]
    AD --> AF

    style E fill:#f66,color:#fff
    style L fill:#f66,color:#fff
    style I fill:#6c6,color:#fff
    style P fill:#6c6,color:#fff
    style X fill:#6c6,color:#fff
    style AF fill:#6c6,color:#fff
```

---

## 6. Share & Public Access

Creating share links with optional password protection, and validating share access tokens.

### Creating a Share

```mermaid
flowchart TD
    A["Create Share Request"] --> B{User is Manager+<br/>on collection?}
    B -->|No| C[403 Forbidden]
    B -->|Yes| D[Generate random token<br/>32 bytes → base64url]

    D --> E[Compute SHA256 hash<br/>of raw token]
    E --> F{Password provided?}

    F -->|Yes| G[BCrypt hash password]
    G --> H[Encrypt password<br/>via Data Protection API]
    H --> I[Store share in DB]
    F -->|No| I

    I --> J["Set share properties:<br/>scope (Collection/Asset),<br/>permissions JSON,<br/>expiration, label"]
    J --> K[Encrypt raw token<br/>via Data Protection API]
    K --> L[Store encrypted token<br/>for admin retrieval]
    L --> M[Audit log: share.created]
    M --> N["Return 201 Created<br/>with raw token (once)"]

    style C fill:#f66,color:#fff
    style N fill:#6c6,color:#fff
```

### Accessing a Share

```mermaid
flowchart TD
    A["Access Share<br/>GET /api/shares/access/{token}"] --> B[Compute SHA256 hash<br/>of provided token]
    B --> C[Query DB by token hash]

    C --> D{Share found?}
    D -->|No| E[404 Not Found]

    D -->|Yes| F{Share revoked?}
    F -->|Yes| G[410 Share Revoked]

    F -->|No| H{Share expired?}
    H -->|Yes| I[410 Share Expired]

    H -->|No| J{Password protected?}
    J -->|No| P[Return shared content]

    J -->|Yes| K{Password provided<br/>in request?}
    K -->|No| L["401 PASSWORD_REQUIRED"]

    K -->|Yes| M{Valid access token?}
    M -->|Yes| P

    M -->|No| N{"BCrypt.Verify<br/>password vs hash?"}
    N -->|No| O["401 UNAUTHORIZED<br/>+ security audit log"]
    N -->|Yes| P

    P --> Q{Share scope?}
    Q -->|Collection| R[Return collection assets<br/>filtered by permissions]
    Q -->|Asset| S[Return single asset<br/>filtered by permissions]

    R --> T["Apply permissions JSON<br/>(download / preview flags)"]
    S --> T
    T --> U[Return 200 OK]

    style E fill:#f66,color:#fff
    style G fill:#f66,color:#fff
    style I fill:#f66,color:#fff
    style L fill:#fc0,color:#000
    style O fill:#f66,color:#fff
    style U fill:#6c6,color:#fff
```

---

## 7. Background Job Processing

Media processing pipeline for uploaded assets, executed by the Wolverine Worker via RabbitMQ.

### Message Dispatch

```mermaid
flowchart TD
    A[Asset Upload Complete] --> B[Publish Wolverine Command<br/>via RabbitMQ]
    B --> C[Worker Consumes Message]
    C --> D[Create DI Scope]
    D --> E[Resolve Scoped Services]
    E --> F{Asset type?}

    F -->|Image| G[ProcessImageAsync]
    F -->|Video| H[ProcessVideoAsync]
    F -->|Document / Audio / Other| I[Mark as Ready<br/>No processing needed]
```

### Image Processing

```mermaid
flowchart TD
    A[ProcessImageAsync] --> B[Download original<br/>from MinIO]
    B --> C[Extract metadata<br/>EXIF / IPTC / XMP]
    C --> D[Auto-populate Copyright<br/>from metadata]
    D --> E["Create thumbnail<br/>ImageMagick resize<br/>(ThumbnailWidth × ThumbnailHeight)"]
    E --> F["Upload thumbnail to MinIO<br/>thumbnails/{assetId}-thumb.jpg"]
    F --> G["Create medium rendition<br/>ImageMagick resize<br/>(MediumWidth × MediumHeight)"]
    G --> H["Upload medium to MinIO<br/>medium/{assetId}-medium.jpg"]
    H --> I["Update asset:<br/>MarkReady(thumbKey, mediumKey)<br/>Status → Ready"]
    I --> J[Audit log: asset.ready]

    B -.->|Error| K[Catch Exception]
    E -.->|Error| K
    G -.->|Error| K
    K --> L["Mark asset as Failed<br/>Status → Failed"]
    L --> M[Audit log:<br/>asset.processing_failed]

    style J fill:#6c6,color:#fff
    style M fill:#f66,color:#fff
```

### Video Processing

```mermaid
flowchart TD
    A[ProcessVideoAsync] --> B[Download original<br/>from MinIO]
    B --> C["Extract poster frame<br/>FFmpeg seek to Ns"]
    C --> D["Upload poster to MinIO<br/>posters/{assetId}-poster.jpg"]
    D --> E["Update asset:<br/>MarkReady(posterKey)<br/>Status → Ready"]
    E --> F[Audit log: asset.ready]

    B -.->|Error| G[Catch Exception]
    C -.->|Error| G
    G --> H["Mark asset as Failed<br/>Status → Failed"]
    H --> I[Audit log:<br/>asset.processing_failed]

    style F fill:#6c6,color:#fff
    style I fill:#f66,color:#fff
```

### Recurring Cleanup Jobs

```mermaid
flowchart TD
    A[Hangfire Scheduler] --> B{Job type?}

    B -->|Stale Upload Cleanup| C["Find assets with<br/>Status = Uploading/Processing<br/>older than 24h"]
    C --> D[Delete from MinIO]
    D --> E[Delete from Database]

    B -->|Expired Share Cleanup| F["Find shares where<br/>ExpiresAt < UtcNow"]
    F --> G[Mark as revoked / delete]

    B -->|Audit Retention| H["Find audit entries<br/>older than retention period"]
    H --> I[Batch delete old entries]

    E --> J[Log cleanup summary]
    G --> J
    I --> J

    J --> K["Per-item try/catch<br/>One failure doesn't<br/>stop the batch"]

    style K fill:#fc0,color:#000
```

---

## Legend

| Symbol | Meaning |
|--------|---------|
| 🟢 Green nodes | Success / happy path endpoints |
| 🔴 Red nodes | Error / rejection responses |
| 🟡 Yellow nodes | Warning / requires attention |
| Solid arrows | Normal control flow |
| Dashed arrows | Error / exception flow |
