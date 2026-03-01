# Observability Guide

This document describes the observability stack used by AssetHub: what each component does, how they connect, security considerations, and operational guidance.

---

## Architecture Overview

```
  AssetHub API ──OTLP gRPC──► Jaeger (traces)
       │                          │
       ├── /metrics ◄── scrape ── Prometheus ──► Grafana (dashboards)
       │                                            │
  AssetHub Worker ──OTLP gRPC──► Jaeger ────────────┘
```

| Component | Purpose | Internal Port | Protocol |
|-----------|---------|---------------|----------|
| **Jaeger** | Distributed trace collector and UI | 4317 (OTLP gRPC), 16686 (UI) | gRPC / HTTP |
| **Prometheus** | Metrics collection via scraping | 9090 | HTTP |
| **Grafana** | Visualization and dashboards | 3000 | HTTP |
| **AssetHub API** | Exports traces (OTLP), exposes `/metrics` | 7252 | HTTPS (dev) / HTTP (prod, behind reverse proxy) |
| **AssetHub Worker** | Exports traces and metrics (OTLP) | N/A | gRPC |

### Data Flow

1. **Traces**: Both the API and Worker export trace spans to Jaeger via OTLP gRPC (`http://jaeger:4317`). Jaeger stores them in Badger (production) or in-memory (development).
2. **Metrics**: The API exposes a `/metrics` endpoint in Prometheus format. Prometheus scrapes this every 15 seconds. The Worker exports metrics via OTLP to Jaeger (no HTTP endpoint).
3. **Dashboards**: Grafana queries Prometheus (for metrics) and Jaeger (for traces) to render dashboards.

---

## Security Model

### Network Isolation

All observability services run on a private Docker bridge network (`internal` in production, `assethub-network` in development). In the production compose:

- **No ports are exposed to the host** for Jaeger, Prometheus, or Grafana (port mappings are commented out).
- Services communicate container-to-container using Docker DNS (e.g., `http://jaeger:4317`).
- Only the API port (7252) is exposed on `127.0.0.1` for the reverse proxy.

**If you need to access Grafana or Jaeger in production**, use one of:
- SSH tunnel: `ssh -L 3000:localhost:3000 yourserver`
- Reverse proxy with authentication (see [Reverse Proxy Rules](#reverse-proxy-rules))
- Temporarily uncomment the port mapping, but bind to `127.0.0.1` only

### Endpoint Protection: `/metrics`

The Prometheus scraping endpoint at `/metrics` is `AllowAnonymous` because the application's `FallbackPolicy` requires authentication on all endpoints by default. Since Prometheus cannot authenticate via OIDC, the endpoint must be anonymous.

**Defense-in-depth**: The `MetricsIpRestrictionMiddleware` rejects any request to `/metrics` that does not originate from a private/loopback IP address (RFC 1918: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`). This means:

- Even if the reverse proxy misconfigures path forwarding, `/metrics` is not accessible from the public internet.
- Prometheus (on the internal Docker network, typically `172.x.x.x`) can scrape without issue.
- If you get a **403 on `/metrics`**, check that the request originates from the internal network.

### OTLP Transport Security

The OTLP endpoint (`http://jaeger:4317`) uses plain HTTP. This is acceptable when:
- All services are on the same Docker bridge network (single host).
- Traffic never leaves the host machine.

**When OTLP must use TLS**:
- If Jaeger (or any OTLP collector) runs on a different host.
- If using a cloud tracing provider (e.g., Grafana Cloud, Datadog, Honeycomb).
- If using `OtlpAuthHeader` (auth tokens over plain HTTP can be intercepted).

**Automated guardrail**: At startup, the application resolves the OTLP endpoint hostname and checks whether it points to a private IP. If the endpoint resolves to a non-private IP over HTTP:
- In **Production**: a `Critical` log is emitted.
- In **Development**: a `Warning` log is emitted.

To switch to TLS, update the endpoint:
```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "https://your-collector.example.com:4317"
  }
}
```

### Prometheus Lifecycle Endpoint

The `--web.enable-lifecycle` flag enables `POST /-/reload` (hot-reload config) and `POST /-/quit` (shut down Prometheus) on the Prometheus HTTP API.

- **Development**: Enabled for convenience. Allows reloading `prometheus.yml` without restarting the container.
- **Production**: **Disabled**. Any container on the internal network could otherwise shut down Prometheus.

To reload Prometheus config in production, send SIGHUP instead:
```bash
docker kill -s HUP assethub-prometheus
```

### Grafana Access Hardening

| Setting | Purpose | Value |
|---------|---------|-------|
| `GF_SECURITY_ADMIN_PASSWORD` | Admin password | **Must be set in `.env`** (no default in production) |
| `GF_USERS_ALLOW_SIGN_UP` | Public registration | `false` |
| `GF_SERVER_ROOT_URL` | Public URL for links/emails | Set to actual URL behind reverse proxy |

Additional hardening options:
- **OIDC integration**: Configure Grafana to authenticate via Keycloak. See [Grafana OIDC docs](https://grafana.com/docs/grafana/latest/setup-grafana/configure-security/configure-authentication/keycloak/).
- **IP restriction**: If Grafana is behind a reverse proxy, restrict access to known admin IPs.
- **Read-only viewers**: Create viewer-only accounts for team members who need dashboards but not admin access.

---

## Reverse Proxy Rules

When placing AssetHub behind a reverse proxy (Nginx, Caddy, Traefik), block these paths from public access:

| Path | Reason | Mitigation |
|------|--------|------------|
| `/metrics` | Exposes runtime metrics, GC stats, request rates | App-level IP restriction + proxy block |
| `/health`, `/health/ready` | Internal health checks (useful for load balancers, not end users) | Proxy block or restrict to monitoring IPs |
| `/hangfire` | Background job dashboard (has admin auth, but belt-and-suspenders) | Proxy block or restrict to admin IPs |

Example Nginx configuration:
```nginx
# Block observability endpoints from public access
location /metrics {
    deny all;
    return 403;
}

location /health {
    # Allow from monitoring systems only
    allow 10.0.0.0/8;
    deny all;
}
```

---

## Exception Recording

The `RecordExceptions` setting controls whether full exception stack traces are attached to trace spans.

| Environment | Default | Behavior |
|-------------|---------|----------|
| Development | `true` | Full stack traces appear in Jaeger — invaluable for debugging |
| Production | `false` | Stack traces are omitted from traces |

**Why disable in production**: Exception messages and stack traces can contain:
- Database connection strings (from `DbException`)
- File system paths (from `IOException`)
- User-supplied data (from validation exceptions)
- Internal class/method names (information disclosure)

Errors are still recorded as span status codes (`Error`); only the exception detail text is suppressed. Use application logs (Serilog) for full exception details in production.

---

## Query String Stripping

The `StripQueryStrings` setting controls whether query strings are removed from HTTP URLs recorded in traces.

| Environment | Default | Behavior |
|-------------|---------|----------|
| Development | `false` | Full URLs including query strings appear in traces |
| Production | `true` | Query strings are stripped; only the path is recorded |

**Why strip in production**: Query strings frequently contain:
- Authentication tokens (`?token=...`)
- API keys (`?key=...`)
- User identifiers and PII (`?email=...`)
- Search terms that may reveal business data

---

## Configuration Reference

All settings are under the `OpenTelemetry` section in `appsettings.json`:

```json
{
  "OpenTelemetry": {
    "Enabled": true,
    "ServiceName": "AssetHub",
    "OtlpEndpoint": "http://jaeger:4317",
    "OtlpAuthHeader": "",
    "EnablePrometheusExporter": true,
    "SamplingRatio": 1.0,
    "BatchSize": 512,
    "ExportTimeoutMs": 30000,
    "RecordExceptions": true,
    "StripQueryStrings": false
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Enabled` | bool | `true` | Master switch for all OpenTelemetry functionality |
| `ServiceName` | string | `"AssetHub"` | Service name in traces and metrics (Worker appends `.Worker`) |
| `OtlpEndpoint` | string | `""` | OTLP collector endpoint (gRPC). Empty disables OTLP export |
| `OtlpAuthHeader` | string | `""` | Auth header for cloud collectors. Format: `header-name=value` |
| `EnablePrometheusExporter` | bool | `true` | Whether to expose the `/metrics` endpoint (API only) |
| `SamplingRatio` | double | `1.0` | Trace sampling ratio. `1.0` = all traces, `0.1` = 10% |
| `BatchSize` | int | `512` | Max spans per OTLP export batch |
| `ExportTimeoutMs` | int | `30000` | OTLP export timeout in milliseconds |
| `RecordExceptions` | bool | `true` | Whether to include exception details in trace spans |
| `StripQueryStrings` | bool | `false` | Whether to remove query strings from traced URLs |

### Environment-Specific Defaults

| Setting | Development | Production |
|---------|-------------|------------|
| `SamplingRatio` | `1.0` (all traces) | `0.1` (10%) |
| `RecordExceptions` | `true` | `false` |
| `StripQueryStrings` | `false` | `true` |
| Prometheus lifecycle | enabled | disabled |
| Jaeger UI port | exposed (16686) | not exposed |
| Grafana port | exposed (3000) | not exposed |

These production defaults are set in `appsettings.Production.json` and `docker-compose.prod.yml`.

### Docker Compose Environment Variables

The following variables can be set in your `.env` file:

| Variable | Default | Description |
|----------|---------|-------------|
| `OTEL_ENABLED` | `true` | Enable/disable OpenTelemetry |
| `OTEL_SAMPLING_RATIO` | `0.1` (prod) | Trace sampling ratio |
| `GRAFANA_ADMIN_USER` | `admin` | Grafana admin username |
| `GRAFANA_ADMIN_PASSWORD` | *(none in prod)* | Grafana admin password — **must be set** |
| `GRAFANA_ROOT_URL` | `http://localhost:3000` | Public URL for Grafana |

---

## Grafana Data Sources

Data sources are auto-provisioned on startup via `docker/grafana/provisioning/datasources/datasources.yml`:

| Name | Type | URL | Notes |
|------|------|-----|-------|
| Prometheus | `prometheus` | `http://prometheus:9090` | Default data source |
| Jaeger | `jaeger` | `http://jaeger:16686` | Trace queries |

To add additional data sources (e.g., Loki for logs), create a new YAML file in `docker/grafana/provisioning/datasources/`.

---

## Troubleshooting

### No traces in Jaeger

1. Verify OpenTelemetry is enabled: `OpenTelemetry:Enabled` must be `true`.
2. Verify the OTLP endpoint is configured: `OpenTelemetry:OtlpEndpoint` must be set.
3. Check the Jaeger container is running: `docker logs assethub-jaeger`.
4. Check the API/Worker logs for OTLP export errors.
5. If sampling ratio is low (e.g., `0.1`), you may need to generate many requests before a trace is captured.

### No metrics in Prometheus

1. Verify the Prometheus exporter is enabled: `OpenTelemetry:EnablePrometheusExporter` must be `true`.
2. Test scraping from inside the Docker network:
   ```bash
   docker exec assethub-prometheus wget -qO- http://api:7252/metrics | head -20
   ```
3. Check Prometheus targets page: `http://localhost:9090/targets` (dev only).
4. Review `docker/prometheus.yml` for correct target configuration.

### 403 Forbidden on `/metrics`

The `MetricsIpRestrictionMiddleware` is rejecting the request because it originates from a non-private IP.

- Ensure Prometheus is on the same Docker network as the API.
- If accessing from a dev machine, the request must come from `127.0.0.1` or a private network IP.
- Check the API logs for `Rejected /metrics request from non-private IP` messages.

### OTLP transport security warning at startup

The application detected that the OTLP endpoint resolves to a non-private IP over HTTP.

- **If intentional** (e.g., testing with a remote Jaeger): Switch to `https://` or accept the risk.
- **If unexpected**: Check that `OtlpEndpoint` points to the correct internal hostname (e.g., `http://jaeger:4317`).

### High trace volume / storage growth

- Reduce `SamplingRatio` (e.g., `0.01` for 1% of traces).
- Reduce `BatchSize` to limit memory usage per export.
- In production, Jaeger uses Badger with configurable retention. Adjust `BADGER_*` environment variables or switch to Elasticsearch/Cassandra for better retention control.
- Prometheus has `--storage.tsdb.retention.time=30d` by default. Adjust as needed.

### Grafana shows "No data"

1. Verify the data source is reachable: Go to Grafana > Configuration > Data Sources > Test.
2. Check that Prometheus is scraping successfully: Grafana > Explore > select Prometheus > query `up`.
3. For Jaeger queries, ensure the service name matches (`AssetHub` or `AssetHub.Worker`).
