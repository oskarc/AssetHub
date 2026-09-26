# Archive

Documents that describe AssetHub as it was **before the 2026-08 reshape**, which cut the product down to
five features (assets, collections, metadata and search, access control, sharing) and replaced Keycloak,
RabbitMQ, Redis and the separate Worker host. They are kept for history and are **not maintained**.
Nothing here describes the current system — for that, start with [CLAUDE.md](../../CLAUDE.md),
[README.md](../../README.md) and [ARCHITECTURE.md](../architecture/ARCHITECTURE.md).

Archived on 2026-09-26 by contract-032 (`.claude/skills/meta-contract-before-execution/CONTRACT-LOG.yaml`).

| Document | What it was | Dated |
|---|---|---|
| [ROADMAP.md](ROADMAP.md) | Commercial-parity feature roadmap. Most of its "Shipped" features were later removed by the reshape, and its open plans assume a public API, Keycloak, RabbitMQ and a Worker host. | 2026-04-18 |
| [FOLLOW-UPS.md](FOLLOW-UPS.md) | Deferred work from shipped roadmap features — mostly for features that no longer exist. | 2026-04 to 2026-08 |
| [COMMERCIAL-DAM-GAP-ANALYSIS.md](COMMERCIAL-DAM-GAP-ANALYSIS.md) | The comparison with commercial DAMs that the roadmap was built from. | 2026-04-18 |
| [APPLICATION-AUDIT.md](APPLICATION-AUDIT.md) | Application audit report (revision 3). | 2026-02-24 |
| [SECURITY-AUDIT.md](SECURITY-AUDIT.md) | Point-in-time security review of the Keycloak-era system. | 2026-02-22 |
| [REVIEW-REPORT.md](REVIEW-REPORT.md) | Review of redundant code and overlapping docs. | early 2026 |
| [test-report.md](test-report.md) | Audit of the E2E suite as it was then. | early 2026 |
| [MICROSERVICE-MIGRATION-ANALYSIS.md](MICROSERVICE-MIGRATION-ANALYSIS.md) | Analysis of splitting into microservices, premised on the Worker host the reshape folded into the API. | 2026 |

## Roadmap IDs in code comments

Some code comments cite roadmap IDs such as `T1-LIFE-01` (trash and purge) or `T5-AUDIT-01` (audit
retention). Those IDs are defined in [ROADMAP.md](ROADMAP.md) here. The comments are kept because the
features they describe are still in the product; the rest of the roadmap is history.
