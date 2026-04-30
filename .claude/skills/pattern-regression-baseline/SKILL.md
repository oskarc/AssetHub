---
name: pattern-regression-baseline
description: Separate new test regressions from pre-existing failures by recording a known-good test baseline and diffing against it on the next run. Use to record a baseline, check for new failures, or decide whether a failing test is "yours" or pre-existing.
---

# AssetHub Regression Baseline

A commit's test result means nothing in isolation. What matters is whether **this change** made anything worse. Pre-existing flakes and long-standing red tests silently accumulate when every author says "not mine." This skill records a named baseline of passing + failing tests and then diffs future runs against it so regressions and improvements are both visible.

## How to run

The skill supports three verbs. If no arg given, pick based on state:
- No baseline file exists → `record`.
- Baseline exists and working tree clean → `check`.
- Baseline exists and working tree dirty → `check` (checks diff against current code).

### `record` — snapshot the current state

1. Run the test suite:
   ```
   dotnet test --configuration Release --no-build --nologo --filter "FullyQualifiedName!~E2E" -- xUnit.MaxParallelThreads=2
   ```
2. Parse the output; for each test, record `(FullyQualifiedName, Passed|Failed|Skipped)`.
3. Capture metadata: commit sha (`git rev-parse HEAD`), branch, UTC timestamp, total counts.
4. Write to `.claude/test-baseline.json`:
   ```json
   {
     "commit": "c5ea695...",
     "branch": "main",
     "recordedAtUtc": "2026-04-19T14:00:00Z",
     "passed": 565,
     "failed": 1,
     "skipped": 0,
     "results": {
       "AssetHub.Tests.Endpoints.AssetEndpointTests.ConfirmUpload_NonExistentAsset_Returns404": "Failed",
       "AssetHub.Tests.Services.AssetServiceTests.DeleteAsync_ValidId_Succeeds": "Passed",
       ...
     }
   }
   ```
5. Confirm to the user: commit sha, counts, and that `.claude/test-baseline.json` is now the reference.
6. **Ensure `.claude/test-baseline.json` is in `.gitignore`** — it's a local reference, not a committed artifact. Add it if missing.

### `check` — diff current run against baseline

1. Run the same test command.
2. Parse outcomes.
3. Classify every test against the baseline:
   - **Regression** — was Passed, now Failed. Attribute to current change. Must fix or document.
   - **Fix** — was Failed, now Passed. Attribute to current change as an improvement.
   - **Pre-existing failure** — was Failed, still Failed. Not this change's problem but report as known debt.
   - **New test** — not in baseline. Mark as new and report pass/fail.
   - **Removed test** — in baseline, not in run. Report for sanity.
4. Summary:
   ```
   Baseline: c5ea695 (565/566), recorded 2026-04-19
   Current : <dirty> (564/566)

   Regressions (1):
     AssetHub.Tests.Services.FooServiceTests.Bar_X_ReturnsY
       Output: <first 3 lines of error>

   Pre-existing failures (1):
     AssetHub.Tests.Endpoints.AssetEndpointTests.ConfirmUpload_NonExistentAsset_Returns404
       Known since: <commit from baseline>

   New tests (3 passing, 0 failing)
   Fixes (0)
   ```
5. **Stop at the first regression** — treat it as the user's problem to fix. Offer to run targeted test + open the stack trace.

### `update` — accept the current run as the new baseline

Use after a regression is intentionally accepted (e.g., a test was removed), or to roll the baseline forward after a clean commit. Same as `record` but requires explicit confirmation because it overwrites.

## Invocation

- `/pattern-regression-baseline` — auto-mode (record if no baseline, check otherwise).
- `/pattern-regression-baseline record`
- `/pattern-regression-baseline check`
- `/pattern-regression-baseline update`

## File format

The baseline is a plain JSON file, not ignored by design if the team wants a shared baseline — but **default is gitignored** so each dev has their own snapshot against their own `main`.

To share a team baseline, commit `.claude/test-baseline.json` explicitly and re-run `record` from a known-good CI commit.

## Rules and gotchas

- **Only two outcomes count: Passed and Failed.** `Skipped` is not a signal — ignore.
- **Flaky tests** — if the same test flaps between runs, record its flakiness count in `flakyHistory` field and surface that warning on `check`. Flaky regressions should be confirmed by rerunning before declaring them real.
- **E2E tests are excluded** by default from the record/check flow — they belong in a separate CI lane and would blow up the baseline time.
- **Parallelism** — always run with `-- xUnit.MaxParallelThreads=2`. AssetHub's xUnit + Testcontainers combo hits EF's ServiceProviderCache warning above that. Until the test fixture is fixed, keep this capped.
- **Baseline commit must be older than the current HEAD** — warn the user if the baseline was recorded *after* HEAD (stale vs. time-travel).
- **Don't auto-fix regressions.** Report them and stop. The fix belongs to the person who caused it.

## Output

Always report:
- Baseline commit + timestamp.
- Current commit (or `<dirty>` if working tree has changes).
- Counts per classification.
- Full test names for regressions (link to stack trace file:line).
- Pre-existing failures collapsed unless the user asks `--verbose`.

## Abort conditions

- `dotnet build` fails — skill stops; regression check is meaningless without a clean build.
- Baseline JSON is malformed — ask the user to delete and re-record.
- Zero-test run (e.g., wrong filter) — skill stops and asks the user to verify the filter.
