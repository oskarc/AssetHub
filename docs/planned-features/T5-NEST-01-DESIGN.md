# T5-NEST-01 — Nested collections — design

**Status:** design draft, not yet implemented.
**Owner:** TBD.
**Decision needed before code starts.**

## Problem

Collections are flat today. A common DAM ask is hierarchy — "Marketing → Campaigns → Spring 2026 → Outdoor". Two real needs drive this:

1. **Navigation.** Browse-by-tree is the muscle-memory pattern from every commercial DAM.
2. **Bulk policy.** "Apply this set of users to everything under Campaigns" is a frequent admin operation. In commercial DAMs (Bynder, Canto, Frontify, AEM, SharePoint, Box) this is the *primary* reason hierarchy exists — admins set permissions at a parent and expect them to apply downwards.

ROADMAP entry [`T5-NEST-01`](./ROADMAP.md) flagged the open design question:

> ACL implications: does a child inherit from its parent?

This doc resolves that question and pins down the data model, UX, and migration path before any code.

## Decision

> **Default-off inheritance, opt-in per child.** Each collection carries an `InheritParentAcl` flag (default `false`). When `true`, the authorization check walks the parent chain to combine ACLs; when `false`, the child is its own root and ignores parents entirely. Walking stops at the first ancestor with `InheritParentAcl = false` — that ancestor's ACL is considered, but its parents are not. This is the **"break inheritance" model** from Adobe AEM and SharePoint, made cheap by being opt-in.

> **A UI affordance ("Apply parent's ACL to this collection") performs a one-shot copy** of the parent's `CollectionAcl` rows into the child's. This stays available alongside the inheritance flag for admins who want a snapshot rather than live inheritance — useful for "seed the child's ACL from the parent, then diverge".

The two mechanisms address different needs:

| Mechanism | Use case |
|---|---|
| `InheritParentAcl = true` | "These permissions should always reflect the parent." Live, ongoing — change parent, child sees the change. |
| "Apply parent's ACL" copy | "Start the child with the parent's permissions but let me edit independently from now on." One-shot, no future link. |

## Why this shape

| Considered | Pros | Cons | Verdict |
|---|---|---|---|
| **(a) No inheritance, no copy** | Trivial; matches today's flat model exactly. | Admins still hand-copy ACLs across child collections at scale. Loses every commercial DAM's bulk-policy pattern. | Too austere. |
| **(a′) No inheritance, UI copy** | Simple runtime; explicit copy gives bulk-policy ergonomics without changing the auth path. | Loses parity with Bynder / Canto / Frontify / AEM. Prospects from those tools look for "inherit from parent" and don't find it. The copy-once-then-drift pattern is unusual in the DAM space. | Initial design draft, superseded. |
| **(b) Inherit-only-when-empty** | Implicit "shape what I need from the parent" magic. | Edit-then-add adds an ACL on the child and silently severs inheritance — invisibly hostile. | Rejected. |
| **(c) Cumulative inheritance, default on** | Industry default (AEM, Bynder, Canto, Frontify). | Pays the runtime cost on every collection, every check. Major rewrite of `ICollectionAuthorizationService`. Cache invalidation becomes "any parent ACL change busts the entire subtree's lookup cache." Needs a materialised effective-permissions cache to stay fast — its own engineering project. | Right model, wrong default. |
| **(c′) Cumulative inheritance with opt-in flag (`InheritParentAcl`)** | Industry-aligned UX without the default-on cost. Most collections stay flat-cost; only inheriting nodes pay the bounded walk. The "break inheritance" UX matches AEM / SharePoint mental model. | Two ACL evaluation paths to test (inherit on / off). Cache invalidation has to cascade-bust descendants when a parent's ACL changes if any descendant inherits. Audit-trail mental model is one level more complex. | **Selected.** |
| **(d) Restrictive inheritance (child ⊆ parent)** | Tightest security; can't accidentally grant more on a child than parent allows. | Cumulative cost of (c) plus an inversion problem. | Rejected. |

The decisive factor: option (c′) gives industry-feature parity (admins from Bynder / Canto don't see a missing capability) while leaving the auth hot path unchanged for any collection that hasn't opted in. Cache invalidation cost is paid only by collections that asked for it.

## Data model

### `Collection` entity

Add two columns:

```csharp
public Guid? ParentCollectionId { get; set; }
public bool InheritParentAcl { get; set; } = false;

public Collection? Parent { get; set; }   // nav property
public ICollection<Collection> Children { get; set; } = new List<Collection>();
```

`InheritParentAcl` defaults to `false` so existing collections (and any newly-created collection where the admin doesn't think about it) stay flat-cost.

### Migration

`AddCollectionParentIdAndInheritFlag`:

```csharp
migrationBuilder.AddColumn<Guid>(
    name: "ParentCollectionId",
    table: "Collections",
    type: "uuid",
    nullable: true);

migrationBuilder.AddColumn<bool>(
    name: "InheritParentAcl",
    table: "Collections",
    type: "boolean",
    nullable: false,
    defaultValue: false);

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

`OnDelete: SetNull` so deleting a parent doesn't cascade-delete its children — they orphan up to root level. Cascade-delete would be too dangerous; if an admin wants children gone too, they delete them explicitly first.

### Cycle prevention

Database-level: PostgreSQL has no built-in cycle constraint on a self-FK. We enforce in the application layer at the point of mutation:

```csharp
private async Task<ServiceError?> ValidateNoCycleAsync(Guid id, Guid? newParentId, CancellationToken ct)
{
    if (newParentId is null) return null;
    if (newParentId == id) return ServiceError.BadRequest("A collection cannot be its own parent.");

    var current = newParentId;
    var depth = 0;
    while (current is not null)
    {
        if (current == id) return ServiceError.BadRequest("Cycle detected: this would make the collection an ancestor of itself.");
        if (++depth > Constants.Limits.MaxCollectionDepth)
            return ServiceError.BadRequest($"Collection depth limit ({Constants.Limits.MaxCollectionDepth}) exceeded.");
        current = await _collectionRepo.GetParentIdAsync(current.Value, ct);
    }
    return null;
}
```

Bounded by `MaxCollectionDepth` so the loop is finite even under attack.

### Depth limit

Add to `Constants.Limits`:

```csharp
public const int MaxCollectionDepth = 8;
```

Validate on `CreateAsync`, `UpdateAsync`, and `SetParentAsync` whenever `ParentCollectionId` is set. Eight is generous for marketing-team taxonomies, keeps the cycle-detection walk + the auth-time inheritance walk bounded.

## Authorization with inheritance

### Algorithm

`ICollectionAuthorizationService.CheckAccessAsync(userId, collectionId, requiredRole, ct)`:

```
current = collectionId
depth = 0
while current is not null and depth <= MaxCollectionDepth:
    if exists CollectionAcl(current, principal-matching-user, effective-role >= requiredRole):
        return true
    inheritFlag, parent = lookup InheritParentAcl + ParentCollectionId for current
    if not inheritFlag:
        break
    current = parent
    depth++
return false
```

The walk stops the moment a node says "I don't inherit". That node's ACL is considered (the loop already checked it before reading the flag), but its parent is not.

For an admin user (`CurrentUser.IsSystemAdmin`) the existing fast-path stays — system admins bypass the walk entirely.

### Batch filter — `FilterAccessibleAsync`

Naive implementation walks per collection: O(n × depth) repository hits. Unacceptable on the hot path.

Better:

1. Pre-load all `(Id, ParentCollectionId, InheritParentAcl)` tuples for collections in scope plus their ancestors. Single query that follows parent IDs up to `MaxCollectionDepth` levels using a recursive CTE or an iterative IN-list expansion.
2. Pre-load all `CollectionAcl` rows where `CollectionId` is in the expanded set, principal matches the user / their groups.
3. In-memory walk per requested collection ID, hitting the local maps.

This pays one CTE round-trip per `FilterAccessibleAsync` call, regardless of depth or fan-out. The cost is bounded by `MaxCollectionDepth × |inheriting collections in scope|`.

For the common case (most collections have `InheritParentAcl = false`), the recursive CTE returns the same set as today and the in-memory walk terminates after one step — practically the same cost as the current flat path.

### `GetUserRolesAsync`

Returns `Dictionary<Guid, string?>` of user's role per collection. Same pre-load + walk shape as the filter; for inheriting collections, the *highest* role found while walking up wins (a user who's Viewer on the child but Manager on an inheriting ancestor is effectively Manager on the child).

### Audit-time question: "what is the user's effective role on collection X?"

Reconstructable from the row data — `CollectionAcl` rows + the inheritance flag on each ancestor are the source of truth. The audit log doesn't need to store the resolved role; the resolver gives it on demand.

## Caching

The pre-load query in `FilterAccessibleAsync` is per-request, scoped, not cached — same as today.

ACL change invalidation:

- Editing `CollectionAcl` rows on collection X invalidates `CacheKeys.Tags.Collection(X)` and `CacheKeys.Tags.CollectionAcl` as today.
- **Plus**: invalidate the same tags for every descendant of X that inherits transitively up through X. The descendant set is computable in one CTE — store the result on the Acl-write path and bust all descendant tags atomically with the write.
- Toggling `InheritParentAcl` on collection X invalidates X's tag (its effective ACL just changed) and all of *its* descendants that inherit through X.

The cascade busts are the only inheritance-specific cost, and they only fire when an admin actually changes ACL or toggles the flag — not on the read path.

## Service layer

### `ICollectionService` additions

```csharp
Task<ServiceResult> SetParentAsync(Guid id, Guid? parentId, CancellationToken ct);
Task<ServiceResult> SetInheritParentAclAsync(Guid id, bool inherit, CancellationToken ct);
Task<ServiceResult> CopyParentAclAsync(Guid id, CancellationToken ct);
```

All admin-only at the service level (they affect collection structure, not asset content).

`SetParentAsync` — moves a collection. Validates cycle + depth. Emits `collection.reparented` audit event. Bust cache tags for the moving collection's descendants — the new parent chain might change effective ACLs.

`SetInheritParentAclAsync` — toggles the flag. Validates that a parent exists if `inherit = true`. Bust cache tags for the collection and its inheriting descendants. Emits `collection.inheritance_enabled` or `collection.inheritance_disabled`.

`CopyParentAclAsync` — copies the parent's `CollectionAcl` rows into this collection's. Behaviour:

1. If `ParentCollectionId is null`: `ServiceError.BadRequest("Collection has no parent.")`.
2. Adds parent's rows that don't already exist on the child. Existing child entries are not touched (we never *remove* on copy).
3. Wraps the inserts and the `collection.acl_copied_from_parent` audit event in `IUnitOfWork.ExecuteAsync` (A-4 atomicity rule).
4. Cache: bust `CacheKeys.Tags.Collection(childId)` and `CacheKeys.Tags.CollectionAcl`.

Note: copying does *not* set `InheritParentAcl = true`. A copy is a snapshot; if the admin wants live inheritance instead, they toggle the flag separately. This is deliberate — the two operations are conceptually different and an admin shouldn't get one when they asked for the other.

### `ICollectionAuthorizationService`

Two changes from today:

1. `CheckAccessAsync` walks the parent chain when the current collection's `InheritParentAcl` is `true`, stopping at the first ancestor with the flag `false`.
2. `FilterAccessibleAsync` pre-loads ancestor tuples + ACL rows in one query, walks in-memory.

The public method signatures don't change. Existing callers see no diff in behaviour for collections that haven't opted in.

## API surface

### New endpoints

```
PATCH  /api/v1/collections/{id:guid}/parent          { parentId: Guid | null }
PATCH  /api/v1/collections/{id:guid}/inherit-acl     { inherit: bool }
POST   /api/v1/admin/collections/{id:guid}/copy-acl-from-parent
```

`PATCH .../parent` and `PATCH .../inherit-acl` — admin-only via `RequireAdmin`. Both apply `ValidationFilter<T>`. Both `MarkAsPublicApi` + `RequireScopeFilter("collections:write")` because admins automating taxonomy setup via API need them.

`POST .../copy-acl-from-parent` — admin-only. Not `[PublicApi]` (admin UX, not part of the integration contract).

### Existing endpoints affected

`GET /api/v1/collections` and `GET /api/v1/collections/{id}` — both gain optional `parentId` and `inheritParentAcl` fields on the response DTO. Backward-compatible.

`POST /api/v1/collections` (`CreateCollectionDto`) — gains optional `ParentCollectionId` and `InheritParentAcl` (default `false`). Backward-compatible.

## UI

### `CollectionTree.razor`

The existing `CollectionBrowser` / `CollectionTree` components already render a flat list with a `Depth` field. Once `ParentCollectionId` is populated, the tree-build helper switches to a recursive walk over the parent FK. Existing rendering with the `└ ` prefix per depth level keeps working.

Visual hint when a collection has `InheritParentAcl = true`: a small chain-link icon next to the name, with a tooltip "Inherits permissions from parent". Removing the link icon = "Break inheritance" action.

### Per-collection ACL management

In `ManageAccessDialog`, when `_collection.ParentCollectionId is not null`, show a panel above the ACL editor:

```
┌────────────────────────────────────────────────────┐
│ Parent: Marketing                                   │
│                                                     │
│ ☐ Inherit access list from Marketing                │
│   Live link — when Marketing's access changes,     │
│   this collection sees it too.                     │
│                                                     │
│ — or, as a one-shot snapshot —                     │
│                                                     │
│ [ Copy parent's access list ]                       │
│   Adds 4 user(s) and 2 group(s) from Marketing.   │
│   Existing entries are kept untouched. After copy, │
│   no link to Marketing remains.                    │
└────────────────────────────────────────────────────┘
```

The two operations are presented as alternatives, not stacked — the wording makes the difference explicit.

When inheritance is on, the ACL editor below shows the *effective* list (own + inherited) with a "Inherited from Marketing" tag on inherited rows, and disables removal of inherited rows. The admin sees what the resolver sees.

When inheritance is on and the admin tries to add a row that already exists at the parent level, snackbar info: "This permission is already inherited from Marketing — adding a direct entry will only matter if you later break inheritance."

Localization keys (en + sv pair, `AdminResource`):

- `Collection_Parent_Hint`
- `Collection_Inherit_Toggle`
- `Collection_Inherit_HelperText`
- `Collection_Parent_CopyAclButton`
- `Collection_Parent_CopyAclHelperText` (with `{0}` parent name and `{1}` user count and `{2}` group count)
- `Collection_Parent_CopyAclConfirm`
- `Collection_Parent_CopyAclSuccess`
- `Collection_InheritedRow_Tag`

### Reparent UI

In the collection edit dialog, add a `MudAutocomplete<CollectionResponseDto>` "Parent collection" field. Excludes the collection itself and its descendants from the candidate list (mirrored client-side; the server still validates).

Adding a parent does **not** auto-enable `InheritParentAcl` — that's a separate, deliberate toggle.

## Migration story for existing data

Zero-touch. All existing collections get `ParentCollectionId = null` and `InheritParentAcl = false` from the migration defaults, behave exactly as before. Admins opt in to nesting and to inheritance separately, per-collection.

## Audit events

Adds three event types to the existing audit pipeline:

| Event | TargetType | `details` |
|---|---|---|
| `collection.reparented` | `collection` | `previous_parent_id`, `new_parent_id` |
| `collection.inheritance_enabled` | `collection` | `parent_collection_id` |
| `collection.inheritance_disabled` | `collection` | `parent_collection_id` |
| `collection.acl_copied_from_parent` | `collection` | `parent_collection_id`, `principals_added` (count) |

(The roadmap's cross-cutting audit table for T5-NEST-01 should be updated to include all four when this lands.)

## Testing

### Unit tests

- `ValidateNoCycleAsync` — single-step cycle, multi-step cycle, deep chain, valid sibling.
- `SetParentAsync` — happy path, self-cycle rejected, depth-limit rejected, missing parent → 404, audit event written.
- `SetInheritParentAclAsync` — flag flip, no-parent rejected when enabling, audit event written, cache invalidation called for descendants.
- `CopyParentAclAsync` — no-parent rejected, fresh copy of N entries, partial overlap doesn't duplicate, atomic with audit, does NOT set `InheritParentAcl`.

### Authorization tests

- Flat default: A → B with `InheritParentAcl = false` on B. Granting on A doesn't grant on B (current behaviour).
- Inheritance on: A → B with `InheritParentAcl = true` on B. Granting Viewer on A → Viewer on B effective. Granting Contributor on A and Viewer on B → Contributor on B effective (highest wins).
- Break inheritance mid-chain: A → B → C with C inherit, B no-inherit. C considers C's ACL + B's ACL, ignores A.
- Depth cap: 9-deep chain with all inheriting. Walk stops at `MaxCollectionDepth = 8`; 9th level isn't considered.

### Cache invalidation tests

- Editing A's ACL when B inherits from A invalidates `Tags.Collection(B)` (verifiable by asserting a subsequent role lookup re-queries).
- Toggling B's inheritance off invalidates `Tags.Collection(B)` and any descendants that inherit through B.

### Integration tests

- Create A → B → C via the API. Authorize a user as Viewer on A. With B and C inheriting: user sees all three. With B not inheriting: user sees A only.
- "Apply parent's ACL" copies parent rows, leaves `InheritParentAcl = false`.
- Bulk delete with nesting: deleting A leaves B with `ParentCollectionId = null` and `InheritParentAcl = false` (orphaned + flat).

## Out of scope

- Default-on inheritance for newly-created child collections. Admins opt in explicitly. (We can revisit if customer demand suggests the default is wrong.)
- Restrictive inheritance (child ⊆ parent).
- Cross-tenant nesting (no tenants exist today).
- Drag-and-drop reparenting in the tree UI. The autocomplete picker is the v1.
- Sibling ordering. Children render alphabetically.
- Soft-deleted parents — collections aren't soft-deletable until T1-LIFE-02 lands.
- Per-asset effective-permissions cache (the AEM-style materialised view). The pre-load + in-memory walk is fast enough at our scale; revisit only if profiling shows otherwise.

## Risks

- **Two evaluation paths to keep correct.** Inherit on / off must both be tested every time the auth path changes. The opt-in nature limits blast radius — most collections are flat-cost — but the inheriting-collections set has to stay first-class in test coverage.
- **Cascade busts on big subtrees.** A parent ACL change with 1000 inheriting descendants busts 1000 cache keys. Acceptable: ACL changes aren't hot-path operations, and the busts happen async post-write. Keep an eye on it via the existing OpenTelemetry `cache.invalidate` span.
- **Drift between inherited rows and copied-from-parent rows.** Acceptable — the UI labels them differently and the operation names ("inherit" vs "copy") signal the difference. Document in the helper text.
- **Depth = 8 is arbitrary.** Eight handles every realistic taxonomy. Logged as a `Warning` if it ever fires — likely a sign of misuse.
- **Inherited ACL rows can't be removed from the child's UI.** This is correct behaviour but might confuse admins who expect "delete" on the row. Mitigated by the inherited-row tag and the disabled state on the delete button.

## Success criteria

After this lands:

- Admin can build a 3-level taxonomy via the UI.
- Authorization performance is identical to today's flat-collection path for any collection with `InheritParentAcl = false` (verifiable via the existing OpenTelemetry `auth.check` span).
- For collections with inheritance on, the walk cost is bounded and visible in telemetry.
- "Inherit access list" toggle and "Copy parent's access list" button are both present in the manage-access dialog, with helper text making the distinction clear.
- Auditing shows `collection.reparented`, `collection.inheritance_enabled`/`_disabled`, `collection.acl_copied_from_parent` — easy to reconstruct the structural and permission history of the taxonomy after any incident.
- Prospects from Bynder / Canto / Frontify don't see a missing capability: "yes, you can inherit permissions from a parent collection."

---

*If this doc still reads sensibly after a week of sitting, write the migration and start the implementation. If it doesn't, the design needs another pass before any code.*
