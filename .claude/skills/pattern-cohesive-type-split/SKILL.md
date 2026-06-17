---
name: pattern-cohesive-type-split
description: When a single type or file has grown large because it fronts many concerns (a facade, composition root, or aggregator) rather than because one responsibility is tangled — split it by relocating along its existing seams into partial files, preserving the single external surface, instead of decomposing it into many types. Use when a file crosses a size budget, when deciding whether "this class is too big" means split-the-file or split-the-design, or when reviewing a god-file.
---

# Splitting a cohesive type without decomposing it

## Principle (why)

"This file is too big" has two different cures, and picking the wrong one does damage.

- **Size from tangled responsibility** — one type doing several jobs that have different reasons to change, different consumers, or different lifetimes. The cure is real decomposition: separate types, each with its own surface. Splitting the *file* here would only hide the tangle.
- **Size from breadth of cohesive concerns** — a type that is *by design* a single surface over many domains: a facade, a composition root, an aggregator, an API client. Its size is the sum of many small, independent members that share one constructor and one external contract. There is no tangle to separate; the single surface is the point.

For the second kind, the cure is **relocation, not decomposition**: move the members into several files of the same type (partial files), grouped by domain, while the external surface — the public contract, the registration, every call site — stays byte-identical. Decomposing such a type into many injected sub-types is the *wrong* fix: it churns every consumer, multiplies registrations, and fights the deliberate "one surface" design that made the type cohesive in the first place.

The judgment call is therefore upstream of the split: **is the size cohesion or tangle?** Answer that first. Only cohesion qualifies for this pattern.

## Pattern (what)

**Partial-file-by-seam, for a confirmed-cohesive type.**

- Keep one **root file**: the constructor (the dependency list), shared private helpers every member uses, and instance fields. This file declares the type once with its full signature and base/interface list.
- Create one **partial file per domain seam**, named `<Type>.<Domain>.<ext>`. Each holds only that domain's members and declares the type with just the `partial` modifier (no constructor, no interface list — those live on the root).
- **Use the seams that already exist.** A god-file of this kind is usually already sectioned — region markers, comment banners, or an obvious clustering of members. Those are the cut lines; do not invent a new taxonomy. (If the same codebase splits other concern-folders by feature, reuse *that* taxonomy — see the layer's organization standard.)
- A shared helper used by only one domain moves *with* that domain's file. A helper used across domains stays on the root.

**Why it's low-risk.** The split is pure relocation: same compiled surface, no signature change, no dependency-injection change. So it needs **no new tests** — the existing build and test suite are the whole gate. A passing build proves member parity (a dropped member would fail to satisfy the contract); the existing tests prove behavior is unchanged. If a split "needs" a test change, it wasn't pure relocation — stop and look for the accidental edit.

**Verification gate:** full-solution build at zero errors / zero new warnings, then the existing suite green. Watch for unused-import warnings the relocation introduces (each new file imports only what its members use) and for references in *other* projects — test projects especially — that bind to the type's namespace.

## Budget (the trigger)

Split before the seams calcify, not after. A file that fronts many concerns gets a size/complexity budget; crossing it is the prompt to relocate along seams *now*, while the cut lines are still clean. The exact threshold is a project choice, but the trigger is structural, not aesthetic: it's "this file holds N independent concern-clusters," not merely "this file is long." A long file that is one cohesive algorithm is not a candidate; a medium file that is five unrelated domains already is.

## When NOT to use this

- The size is one method doing too much → extract methods / a helper type, not partials.
- The members have genuinely different consumers, lifetimes, or reasons to change → that's tangle; decompose into separate types with their own surfaces.
- The type is small enough to read in one sitting → a budget exists to prevent god-files, not to fragment healthy ones. Splitting a cohesive 200-line type into ten files trades readability for ceremony.
