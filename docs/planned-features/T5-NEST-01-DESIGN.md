# T5-NEST-01 — Nested collections — design

**Status:** design draft, not yet implemented.
**Owner:** TBD.
**Decision needed before code starts.**

## Problem

Collections are flat today. A common DAM ask is hierarchy — "Marketing → Campaigns → Spring 2026 → Outdoor". Two real needs drive this:

1. **Navigation.** Browse-by-tree is the muscle-memory pattern from every commercial DAM.
2. **Bulk policy.** "Apply this set of users to everything under Campaigns" is a frequent admin operation that today requires per-collection ACL editing.

ROADMAP entry [`T5-NEST-01`](./ROADMAP.md) flagged the open design question:

> ACL implications: does a child inherit from its parent?

This doc resolves that question and pins down the data model, UX, and migration path before any code.

## Decision

> **Collections stay flat for ACL purposes.** Children do not inherit ACLs from parents at runtime. The hierarchy is purely a navigation structure.

> **A UI affordance ("Apply parent's ACL to this collection") performs a one-shot copy of the parent's `CollectionAcl` rows into the child's** at the moment the admin clicks it. After the copy, the child's ACL is owned independently by the child — diverging the parent's ACL doesn't affect previously-copied children.

This is **option (a) "no inheritance" with a deliberate "copy-once" UX shortcut**, not options (b) / (c) / (d) from the chat thread.

## Why this shape

| Considered | Pros | Cons | Verdict |
|---|---|---|---|
| **(a) No inheritance, no copy** | Trivial; matches today's flat model exactly. | Admins still hand-copy ACLs across child collections at scale. | Too austere; loses the bulk-policy gain. |
| **(b) Inherit-only-when-empty** | Implicit "shape what I need from the parent" magic. | Edit-then-add adds an ACL on the child and silently severs inheritance — invisibly hostile. Auditing becomes "what *would* this user see?" instead of "what does the row say?" — much harder to reason about. | Rejected. |
| **(c) Cumulative inheritance** | Easiest mental model: child sees parent's grants plus its own. | Per-asset auth check has to walk the parent chain on every request, multiplying cache invalidation cost. Revoking access on the parent silently affects every nested level — risk of accidental over-permissioning lasting longer than the admin meant. | Rejected. |
| **(d) Restrictive inheritance (child ⊆ parent)** | Tightest security; can't accidentally grant more on a child than parent allows. | Cumulative cost of (c) plus an inversion problem when admins want a sub-collection a colleague *wouldn't* see at the parent level. UX gets weird ("why can't I add Bob to Spring 2026 when he's not on Marketing?"). | Rejected. |
| **(a) + UI "Apply parent ACL" copy** | Flat ACLs at runtime — auth check is unchanged, cache invalidation is unchanged, audit trail is unchanged. Hierarchy is opt-in shortcut at edit time. Admins get bulk-grant ergonomics without invisible runtime magic. | Slight divergence over time — parent and copied-from-parent children drift. Acceptable: the admin copied once, knowingly, and re-syncing is a one-click operation. | **Selected.** |

The decisive factor is **runtime simplicity**. The current `ICollectionAuthorizationService` is a hot path — `CheckAccessAsync` and `FilterAccessibleAsync` get called on practically every authenticated request. Touching the ACL evaluation algorithm to walk a parent chain would force re-validation of every cached role lookup, every endpoint's ACL filter, every collection-scoped audit. The hierarchy adds zero authorization cost in option (a)+copy.

## Data model

### `Collection` entity

Add one nullable column:

```csharp
public Guid? ParentCollectionId { get; set; }
public Collection? Parent { get; set; }   // nav property
public ICollection<Collection> Children { get; set; } = new List<Collection>();
```

### Migration

`AddCollectionParentId`:

```csharp
migrationBuilder.AddColumn<Guid>(
    name: "ParentCollectionId",
    table: "Collections",
    type: "uuid",
    nullable: true);

migrationBuilder.CreateIndex(
    name: "idx_collections_parent_id",
    table: "Collections",
    column: "ParentCollectionId");

migrationBuilder.AddForeignKey(
    name: "FK_Collections_Collections_ParentCollectionId",
    table: "Collections",
    column: "ParentCollectionId",
    principalTable: "Collections",
    principalColumn: "Id",
    onDelete: ReferentialAction.SetNull);
```

`OnDelete: SetNull` so deleting a parent doesn't cascade-delete its children — they orphan up to root level. Cascade-delete would be too dangerous; if an admin wants children gone too, they delete them explicitly first (the existing bulk-delete dialog handles this once it's wired).

### Cycle prevention

Database-level: PostgreSQL has no built-in cycle constraint on a self-FK. We enforce in the application layer at the point of mutation:

```csharp
private async Task<ServiceError?> ValidateNoCycleAsync(Guid id, Guid? newParentId, CancellationToken ct)
{
    if (newParentId is null) return null;
    if (newParentId == id) return ServiceError.BadRequest("A collection cannot be its own parent.");

    // Walk the proposed ancestor chain — if `id` appears, it'd be a cycle.
    var current = newParentId;
    while (current is not null)
    {
        if (current == id) return ServiceError.BadRequest("Cycle detected: this would make the collection an ancestor of itself.");
        current = await _collectionRepo.GetParentIdAsync(current.Value, ct);
    }
    return null;
}
```

Bounded by the configured `MaxCollectionDepth` (see below) so the loop is finite even under attack.

### Depth limit

Add to `Constants.Limits`:

```csharp
public const int MaxCollectionDepth = 8;
```

Validate on `CreateAsync` and `UpdateAsync` whenever `ParentCollectionId` is set. Eight is generous for marketing-team taxonomies and keeps the cycle-detection walk + tree-render bounded.

## Service layer

### `ICollectionService` additions

```csharp
Task<ServiceResult> SetParentAsync(Guid id, Guid? parentId, CancellationToken ct);
Task<ServiceResult> CopyParentAclAsync(Guid id, CancellationToken ct);
```

Both admin-only at the service level (they affect collection structure, not asset content).

`SetParentAsync` — moves a collection. Validates cycle + depth. Emits `collection.reparented` audit event with `previous_parent_id` and `new_parent_id` (already specified in the cross-cutting audit table for T5-NEST-01).

`CopyParentAclAsync` — copies the parent's `CollectionAcl` rows into this collection's. Behaviour:

1. If `ParentCollectionId is null`: return `ServiceError.BadRequest("Collection has no parent.")`.
2. If the child already has ACL rows: confirmation prompt is the UI's job. The service blindly adds parent's rows that don't already exist on the child. Existing child entries are not touched (we never *remove* on copy — that's the admin's call).
3. Wraps the inserts and the `collection.acl_copied_from_parent` audit event in `IUnitOfWork.ExecuteAsync` (A-4 atomicity rule).
4. Cache invalidation: tag-bust `CacheKeys.Tags.Collection(childId)` and `CacheKeys.Tags.CollectionAcl` so role lookups refresh.

### `ICollectionAuthorizationService`

**No changes.** Authorization stays flat. `CheckAccessAsync(userId, collectionId, role, ct)` still reads only `CollectionAcl` rows for `collectionId`, doesn't walk parents, doesn't merge.

This is the load-bearing line of the whole design — the hot path is unchanged.

## API surface

### New endpoints

```
PATCH  /api/v1/collections/{id:guid}/parent          { parentId: Guid | null }
POST   /api/v1/admin/collections/{id:guid}/copy-acl-from-parent
```

`PATCH .../parent` — admin-only via `RequireAdmin`. The body is a one-field DTO so future fields (e.g. `position` for sibling ordering) don't break the contract. ValidationFilter applies. `MarkAsPublicApi` + `RequireScopeFilter("collections:write")` because reparenting *is* a structural change clients automating taxonomy management need.

`POST .../copy-acl-from-parent` — admin-only. Not `[PublicApi]` (admin UX surface, not part of the integration contract).

### Existing endpoints affected

`GET /api/v1/collections` and `GET /api/v1/collections/{id}` — both gain an optional `parentId` field on the response DTO. Backward-compatible because it's nullable + new.

`POST /api/v1/collections` (`CreateCollectionDto`) — gains optional `ParentCollectionId`. Backward-compatible.

## UI

### `CollectionTree.razor`

The existing `CollectionBrowser` / `CollectionTree` components already render a flat list with a `Depth` field. Once `ParentCollectionId` is populated, the tree-build helper switches from "all root, depth 0" to a real recursive walk over the parent FK. Existing rendering with the `└ ` prefix per depth level keeps working unchanged.

### "Apply parent's ACL" affordance

In the per-collection `ManageAccessDialog`, when `_collection.ParentCollectionId is not null`:

```
┌────────────────────────────────────────────┐
│ This collection has a parent: Marketing.   │
│                                            │
│ [ Copy parent's access list ]              │
│                                            │
│ Adds 4 user(s) and 2 group(s) from         │
│ Marketing's access list. Existing entries  │
│ on this collection are kept untouched.     │
└────────────────────────────────────────────┘
```

Click → confirm dialog showing exactly which principals will be added and at what role → call `POST /admin/collections/{id}/copy-acl-from-parent` → snackbar success → reload the dialog's ACL list.

Localization keys (en + sv pair, `AdminResource`):

- `Collection_Parent_Hint`
- `Collection_Parent_CopyAclButton`
- `Collection_Parent_CopyAclConfirm` (with `{0}` parent name and `{1}` user count and `{2}` group count)
- `Collection_Parent_CopyAclSuccess`

### Reparent UI

In the collection edit dialog, add a `MudAutocomplete<CollectionResponseDto>` "Parent collection" field. Excludes the collection itself and any of its descendants from the candidate list (the cycle-prevention rules above, mirrored client-side for ergonomics; the server still validates).

## Migration story for existing data

Zero-touch. All existing collections get `ParentCollectionId = null` from the migration default, behave exactly as before. Admins opt in to nesting per-collection.

If we ever decide to seed an initial hierarchy from filesystem-style names (e.g. collections named "Marketing/Campaigns/Spring 2026" → split on `/`), that's a separate one-shot migration tool, not part of this feature.

## Audit events

Already specified in the ROADMAP cross-cutting table for `T5-NEST-01`:

- `collection.reparented` — TargetType `collection`, details `previous_parent_id`, `new_parent_id`. Emitted from `SetParentAsync`.

Add one more:

- `collection.acl_copied_from_parent` — TargetType `collection`, details `parent_collection_id`, `principals_added` (count). Emitted from `CopyParentAclAsync`. Justifies the cross-cutting "audit the decision, not every intermediate state" rule — one event per copy, not N events for N principals.

## Testing

### Unit tests

- `ValidateNoCycleAsync` — single-step cycle, multi-step cycle, deep chain, valid sibling.
- `SetParentAsync` — happy path, self-cycle rejected, depth-limit rejected, missing parent → 404, audit event written.
- `CopyParentAclAsync` — no-parent rejected, fresh copy of N entries, partial overlap (some entries already exist) doesn't duplicate, atomic with audit.

### Integration tests

- Create A → B → C, then move B under C — should fail with cycle detection.
- Create A with 3 ACL entries, create B as child of A, copy ACL — B has the same 3 entries, A's 3 untouched.
- Authorization unchanged: granting on parent does NOT grant on child (no inheritance).
- Bulk delete with nesting: deleting A leaves B with `ParentCollectionId = null`, doesn't cascade-delete B.

## Out of scope

- ACL inheritance at runtime (any flavour). If a customer demands it post-launch, the door isn't closed — the parent FK is in place — but the change would be a separate Tier-0-level redesign of the auth path.
- Cross-tenant nesting (no tenants exist today).
- Drag-and-drop reparenting in the tree UI. The `MudAutocomplete` "Parent collection" picker is the v1 UX; drag-and-drop is a follow-up.
- Sibling ordering. Children render alphabetically. If admins demand explicit ordering, add `SortOrder` later — not now.
- Soft-deleted parents. If a parent is in Trash, its children orphan to `ParentCollectionId = null` immediately at soft-delete time. (T1-LIFE-01 only soft-deletes assets; collections aren't soft-deletable until T1-LIFE-02 lands.)

## Risks

- **Drift between parent and copied-from-parent children.** Acceptable — the operation is named "copy", not "link". Document in the UI hint.
- **Depth = 8 is arbitrary.** Eight handles every realistic taxonomy (most marketing teams stop at 3–4). If anyone hits the cap they're using collections wrong. Logged as a `Warning` if it ever fires.
- **Reparenting changes the breadcrumb a user is currently looking at.** The detail page should refresh on `OnLocationChanging`; existing components already handle this for renames.

## Success criteria

After this lands:

- Admin can build a 3-level taxonomy via the UI.
- Authorization performance is identical to today's flat-collection path (verifiable via the existing OpenTelemetry `auth.check` span).
- "Copy parent's access list" is the documented bulk-policy shortcut. Marketing teams can stamp out a multi-level taxonomy with a few clicks instead of hand-copying ACLs per collection.
- Auditing shows one `collection.reparented` per move and one `collection.acl_copied_from_parent` per copy — easy to reconstruct the structural history of the taxonomy after any incident.

---

*If this doc still reads sensibly after a week of sitting, write the migration and start the implementation. If it doesn't, the design needs another pass before any code.*
