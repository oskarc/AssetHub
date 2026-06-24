---
name: pattern-enum-string-persistence
description: Persist enums as explicit strings via a paired to-string / from-string extension method defined alongside the enum — never the numeric value or the framework's name. Use when adding an enum that will be stored, sent over the wire, read back from a database, or accepted as a caller-supplied token at a request boundary.
---

# Enum ↔ string persistence

## Principle (why)

An enum's *numeric* value is an accident of declaration order; its framework-generated *name* is an accident of identifier spelling. Persisting either couples your stored data to something you'll eventually want to change — reorder the members, rename one for clarity, insert a value in the middle — and the change silently corrupts every existing row. An **explicit string mapping**, written by hand, decouples the stored representation from the code: the wire/DB value is a deliberate contract you control, and the code is free to evolve as long as the mapping is maintained.

It also makes stored data legible. `"in_review"` in a column is self-describing; `3` is a lookup.

## Pattern (what)

For every enum that crosses a persistence or wire boundary, define a paired conversion as extension methods **alongside the enum** (same file, so they travel together and can't drift apart):

```csharp
public static string ToDbString(this ExampleStatus s) => s switch {
    ExampleStatus.Draft    => "draft",
    ExampleStatus.InReview => "in_review",
    ExampleStatus.Approved => "approved",
    _ => throw new ArgumentOutOfRangeException(nameof(s))
};

public static ExampleStatus ToExampleStatus(this string s) => s switch {
    "draft"     => ExampleStatus.Draft,
    "in_review" => ExampleStatus.InReview,
    "approved"  => ExampleStatus.Approved,
    _ => throw new ArgumentOutOfRangeException(nameof(s)) // or a safe Unknown fallback for DB reads
};
```

- The string values are an explicit, stable contract — chosen for readability (lower_snake), not derived from the member name.
- The two directions live next to the enum and are exhaustive `switch`es over the members.
- The persistence layer wires the converter at the column (`.HasConversion(v => v.ToDbString(), v => v.ToX())`).
- **The trust level of the *source* decides what an unknown string means:**
  - *from code* (a literal, an internal call) → **throw**; an unmapped value is a bug.
  - *read back from storage* (possibly written by a newer version) → may map the unrecognized string to a designated `Unknown` member rather than throw, for forward-compat.
  - *from an untrusted boundary* (a caller-supplied API/DTO filter token) → **guard with the validator and ignore unknowns**; a malformed request must never reach the throwing converter and surface as a 500. A bad request is not a server bug.

## Implementation constraints (how)

- **Never** persist `(int)` casts or `.ToString()` / `Enum.Parse` on the member name.
- When a third side exists — a validator that answers "is this string a legal value?" — keep it in the *same* family as the converters and test all three together for exhaustive agreement. (Adding an enum member and updating only the converters, leaving the validator behind, is a classic silent gap; see the string-enum-triple rule in test scaffolding.)
- **At an untrusted boundary, guard-before-parse, on every dimension.** The canonical shape is `tokens.Where(IsValidX).Select(ToX)` — the validator runs *before* the throwing converter. When a boundary accepts more than one token dimension, guard them all the same way: guarding one and letting a sibling dimension reach the raw converter is a latent 500, and the asymmetry between the two is itself the tell.

## Boundaries

- This is for enums that are **stored or transmitted**. A purely in-memory enum (a method flag that never leaves the process) doesn't need a string mapping.
- The mapping is a contract: changing a string value is a data migration, not a refactor.
