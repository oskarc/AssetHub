---
name: pattern-time-series-rollup
description: When you need an aggregate over time-bounded source events (audit logs, analytics events, message deliveries) that survives source pruning. Pre-aggregate into a rollup table keyed on (date, metric, entity), upsert-idempotent, with a self-healing cron loop and a back-fill on first start. Use when the source table is high-volume and has a retention policy that's tighter than the analytic horizon.
---

# Time-series rollup pattern

Most analytics features start as "let's just GROUP BY on the audit table." That works until the audit table has a retention policy that's tighter than the analytic horizon — and then your "downloads in 2024" chart starts losing data the moment retention sweeps. The rollup pattern flips the relationship: the rollup *replaces* the source for the time range it covers, so source retention can be aggressive without losing the analytic.

Announce in chat whether you're running this skill or skipping it, and why.

## When to run

Reach for this pattern when **all** of:
- The source is event-shaped (audit log, message-delivery log, watermark download log, anything write-heavy + read-rare).
- The source has a retention policy that's tighter than the question being asked. ("Top assets last year" can't be answered from an audit table that purges at 90 days.)
- The query the dashboard / report needs is a GROUP BY over time × entity × metric — not a row-level lookup.

Skip for:
- Live-state queries (`SELECT * FROM Assets WHERE ...`). The rollup is for *aggregates*, not row reads.
- Source tables with no retention policy. If audit lives forever, GROUP BY at read time is fine until proven otherwise.
- Sub-day granularity needs. The pattern is daily; if the question is "downloads per hour," either build a parallel hourly rollup table or stay on-demand.
- Single-tenant tools with bounded data volumes. A 10k-row audit table doesn't need pre-aggregation.

## Principles (why)

### 1. The rollup outlives its source — by design.

Once a day is rolled, the source rows are no longer authoritative for that range. This is the whole point: source retention can be tight, the rollup keeps the analytic. Audit retention, message-delivery purge, log compaction — none of them touch the rollup table.

The corollary: writes to the rollup must be **complete** before the source can be safely pruned. Schedule the rollup before the retention sweeper, not parallel to it.

### 2. Idempotent on `(time, metric, entity)`.

Re-running for the same time slice is the central correctness primitive. It enables:
- Retry on failure without double-counting.
- Back-fill of missed days without bookkeeping.
- Self-healing cron loops that don't need a "did I already run today?" lock.

If your rollup write isn't idempotent on `(time, metric, entity)`, you don't have this pattern — you have a different one with worse failure modes.

### 3. Back-fill is part of the contract, not an afterthought.

A rollup feature that only forward-rolls is broken — every fresh deployment, every database restore, every dev-environment provisioning ships an empty dashboard until the first cron tick fires. Back-fill on first-start solves this once.

Cap the back-fill window (a configurable number of days) so first-start cost is bounded; longer windows can be a manual trigger.

### 4. Self-heal beats precise scheduling.

Don't depend on "the cron must fire at exactly 02:00 UTC" — a worker pod restarting at 01:59 will miss it. Run the rollup loop **hourly**, and have each tick check "is yesterday already rolled?" If yes, no-op; if no, roll it. This handles missed cron windows, multi-pod deployments, and process restarts without explicit coordination.

## Patterns (what)

### The schema

```
RollupTable {
  Date         (calendar day, UTC)
  Metric       (enum / string discriminator — what's being counted)
  EntityId     (string — id of the thing being aggregated; flexible enough to hold a Guid hex, a hash, an enum-name)
  Value        (long count or sum)
  UpdatedAt    (when the row was last written)

  PK (Date, Metric, EntityId)
  Index (Date, Metric)
}
```

The composite PK is load-bearing. It encodes the "one row per (day, metric, entity)" invariant directly in the schema. Upsert semantics fall out of it — most databases give you a clean ON CONFLICT or MERGE on a composite key.

`EntityId` is a *string*, not a typed FK. The rollup outlives FK targets (assets get deleted, recipients age out) and may aggregate over things that aren't entities (an asset-type name, a date bucket). Treat it as opaque.

### The write path

Two equally-valid shapes; pick one based on row count:

- **Delete-then-insert per slice** (small slices): `DELETE WHERE Date = @day AND Metric = @metric; INSERT ...`. Simple, atomic, idempotent. Use when a day's slice is < a few thousand rows.
- **Upsert / MERGE** (large slices): `INSERT ... ON CONFLICT (Date, Metric, EntityId) DO UPDATE SET Value = EXCLUDED.Value`. Cheaper for big slices. Use when a day produces hundreds of thousands of rows.

In both cases the write is per-slice, not per-row from-scratch. That's what makes re-running for the same day cheap.

### The cron loop

```
ExecuteAsync:
  if not enabled: return
  if first start: backfill last N days where rollup is missing
  loop hourly:
    yesterday = today.AddDays(-1)
    if rollup table doesn't have yesterday: roll yesterday
    else: skip
```

The "is yesterday rolled?" check is cheap (it's an indexed lookup on `(Date, Metric)`) and runs every tick. The actual rollup runs at most once per day per pod.

### The audit row

Emit one audit event per successful rollup tick: `{rollup_completed, target = analytics, details = {date, rows_written}}`. This isn't load-bearing for the rollup itself — it's the bridge between source-retention and rollup-correctness. When a retention auditor asks "did the rollup capture this day before audit purged the events?", you can prove it.

### Drilling back to entity names

Rollup rows store opaque IDs; dashboards want display names. Hydrate at read time via a separate join on the live tables — don't denormalise names into the rollup. The rollup is forensic; names change.

For soft-deleted entities (where the live row still exists with a deleted flag), join via `IgnoreSoftDelete` so you can answer "this leaked asset was downloaded N times before it was removed." For hard-deleted entities, surface `(deleted)` from a missing-row sentinel, not from the rollup itself.

## Implementation constraints (how)

These are stack-agnostic but worth pinning:

- **Time zone**: store Date in UTC. Storing in local time creates a DST gap twice a year that the rollup can't reason about.
- **Retention coordination**: the rollup MUST run before the source retention sweeper for any given day. If you run them in parallel, you'll get partial-day rollups that get superseded on the next tick — benign but noisy. Better: rollup at hour H, retention at hour H+1 (configurable).
- **Concurrency**: multi-pod deployments running this loop are safe because of idempotency. Two pods racing on the same day produce the same result. If you grow to where this is wasted CPU, swap the cron-loop for a `SELECT ... FOR UPDATE SKIP LOCKED` claim on a "rollup queue" table — but that's a later optimization, not table-stakes.
- **Schema evolution**: adding a new metric is a new enum value + a new GROUP BY. Not a schema change. The composite-PK rollup table is metric-pluggable by design.

## Anti-patterns to avoid

- **Forward-rollup-only**. No back-fill, no historical recovery. Looks fine until your dev environment ships an empty dashboard.
- **Per-row upsert in a loop**. Slow and not atomic. The slice-level write is the right granularity.
- **Storing display names in the rollup**. They drift; the rollup becomes a stale-name source.
- **Tying rollup retention to source retention**. The rollup outlives the source by definition. If you purge it on the same schedule as audit, you've defeated the pattern.
- **Hourly granularity smuggled in via the date field** (storing `2026-05-08T13:00:00Z` and pretending it's a day). Either you're doing daily rollup or you're not. Use a separate hourly rollup table.
- **Skipping the audit row**. Without it, you can't prove the rollup outlived its source — which is the whole point of the pattern.
