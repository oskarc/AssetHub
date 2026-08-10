---
name: principle-information-architecture
description: How to decide a web app's navigational structure — page hierarchy, where a sub-view lives, how nav is grouped, and how users keep their bearings. The design step that creates structure, upstream of the heuristic audit that checks it. Use when adding a page, deciding tabs-vs-routes, grouping a nav menu, introducing a new section or audience, or reviewing whether a growing area still has a usable map.
---

# Information architecture

## Principle (why)

Bad IA fails silently. Users don't report "I couldn't form a map of this app" — they just navigate slower, miss features, and bookmark nothing. Because the cost never shows up as a bug, IA decisions get made by accretion: each feature lands wherever the last one did, and a year later the navigation is a record of build order rather than a map of what the product does.

So the standard fixes a few load-bearing decisions up front. The throughline: **structure follows the user's mental model and the shape of the routes — never the order features were built.** Each principle below is a place where the cheap default (one more tab, one more flat link, one more conditional in the layout) quietly degrades the map.

### A — One shell per audience/mode
A navigational shell serves one audience in one mode. When an app spans distinct modes — an authenticated workspace, an admin console, an anonymous public view — give each its own layout rather than one shell that toggles its chrome by role. Conditional chrome accumulates branches no one can fully reason about ("is this link visible here?"); a separate shell states the mode in one place and shares nothing it shouldn't. The boundary: a "mode" is a different *navigation model* and different chrome, not merely a link that's hidden for some roles.

### B — Routable sub-pages over a tabbed god-page
A section's sub-views earn their own routes once any of these is true: someone would **deep-link** to one, a sub-view needs its **own permission**, or the **count** has passed what a glance can hold. Tabs in a single page can't be bookmarked, force per-tab authorization into conditional rendering, and grow into a monolith that owns every concern in the section. Converting tabs → routes makes each sub-view addressable, independently authorizable, and independently ownable.

### C — Group navigation by goal, with a flat-link cap
A flat list of nav links stops being scannable at roughly seven. Past the cap, group the links by the user's **goal** — what they're trying to accomplish — not alphabetically and not by the data type behind them. Name each group for the intent it serves. A well-grouped nav reads as a map of the product's jobs-to-be-done; a flat or type-sorted one makes the user linearly scan for the verb they want.

### E — Depth requires wayfinding
Every level of route nesting a user can reach needs an affordance answering "where am I, and how do I get back" — a breadcrumb trail or a contextual back. Depth without wayfinding strands the user at a deep URL with no map up. This principle is often *aspirational*: an app grows nested routes feature by feature while the breadcrumb layer never gets built. That mismatch is wayfinding debt — name it and pay it down, don't let depth accumulate silently.

## Pattern (what)

- **Shell-per-audience** — one layout component per mode; the public/anonymous shell shares no nav with the authenticated shell.
- **Tabs→routes threshold** — convert when *deep-linkable* OR *independently permissioned* OR *past the glance cap*. Any one is enough.
- **Cap-then-group** — flat list up to ~7; beyond, intent-named groups. Reuse the feature names the rest of the layer already uses (routes, source tree) rather than inventing a nav-only taxonomy — naming consistency across routes/nav/source-tree is the UI standard's "one feature taxonomy" pattern; defer to it, don't restate it.
- **Breadcrumb-on-depth** — any route deeper than its section root carries a breadcrumb or a contextual back to the section root.
- **Derive-then-override (how to build the trail)** — derive the trail from *structure* wherever the structure carries the label: the route plus the navigation names already say "Admin › Users", so build that trail once in the section's shell and every page below inherits it with no per-page code, in a consistent place, staying in sync with the nav from a single source. Fall back to a per-page trail *only* for the one segment whose label is runtime data — an entity's title known only to that page. Auto-derivation is the default; per-page is the scoped exception for dynamic leaves, not the norm.

## Boundaries

- IA is the **decision step that creates structure**. Auditing existing structure against usability heuristics (contrast, affordances, error recovery) is a *different* concern — that's the UX-check skill. This skill is upstream of it.
- One feature taxonomy across routes, nav, and source tree is owned by the UI-standard's taxonomy pattern. This skill points at it for naming and does not duplicate it.
- A small app with one audience and a shallow route tree may legitimately need none of this. These principles earn their keep as an app grows a second audience, a section that sprouts sub-views, or a nav list past the cap — apply them at those inflection points, not pre-emptively.
