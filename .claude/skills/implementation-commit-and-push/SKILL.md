---
name: implementation-commit-and-push
description: "Thoroughly review all staged/unstaged changes against AssetHub conventions, run build and tests (fixing failures), compose a why-focused commit message, and push. Use when the user asks to commit, push, or ship changes."
---

# AssetHub Commit & Push

End-to-end quality gate: review → build → test → fix → commit → push. Nothing ships without passing every phase.

## How to run

Execute phases 1–9 in order. **Stop and fix** when a phase fails — do not skip ahead. After fixing, re-run the failed phase before continuing.

---

## Phase 1: Inventory changed files

1. Run `git status --porcelain` to list all modified, added, and deleted files.
2. Run `git diff --name-only` (unstaged) and `git diff --cached --name-only` (staged).
3. If nothing is staged, stage all changes with `git add -A` after confirming with the user.
4. Group files by layer:
   - **Domain** — `src/AssetHub.Domain/`
   - **Application** — `src/AssetHub.Application/`
   - **Infrastructure** — `src/AssetHub.Infrastructure/`
   - **API** — `src/AssetHub.Api/`
   - **UI** — `src/AssetHub.Ui/`
   - **Worker** — `src/AssetHub.Worker/`
   - **Tests** — `tests/`
   - **Config/Docs/Other** — everything else
5. Read every changed file in full — do not skim.

---

## Phase 2: Quality guardrails review

Walk through `.github/instructions/quality-guardrails.instructions.md` for each changed file, applying only the checks relevant to that file's layer:

- **Blazor UI** — a11y (alt, aria-label, PageTitle, color+icon), optimistic UI, ConfirmDialog, localization parity, MudBlazor-only.
- **API endpoints** — auth policy, antiforgery, route constraints, ValidationFilter, `.ToHttpResult()`.
- **Services / repos** — no raw SQL, sanitized filenames, CacheKeys+tags, ServiceResult, scoped services.
- **DTOs** — DataAnnotations on all user-bound fields, nullable ref types.
- **Configuration** — SectionName, no hardcoded secrets, ValidateOnStart.
- **Worker** — per-item try/catch, CancellationToken, IServiceScopeFactory, log counts.

If findings exist, fix them now. Re-read the file after fixing to confirm.

---

## Phase 3: Security review

For every changed file, check against the security conventions in `.github/prompts/security-review.prompt.md`:

- No hardcoded secrets or credentials anywhere (grep for patterns: `password`, `secret`, `apikey`, `connectionstring` in literals).
- No `FromSqlRaw` / `FromSqlInterpolated` / string-built SQL.
- Authorization policies present on new/changed endpoints.
- Input validation on new/changed DTOs.
- File paths sanitized if derived from user input.
- `CurrentUser` used — never `HttpContext.User` directly.
- `RoleHierarchy` methods — no hardcoded role strings.

If findings exist, fix them now.

---

## Phase 4: Architecture compliance

Verify layer dependency rules are not violated:

- Domain references nothing.
- Application references only Domain.
- UI references only Application — never Infrastructure or Api.
- Infrastructure references Application + Domain.
- Api and Worker are composition roots.

Check for patterns that are **not used** in AssetHub:
- Domain events, value objects, specifications, FluentValidation, Swagger/OpenAPI, third-party state management.

If violations found, fix them now.

---

## Phase 5: Self-check validation

Run through `.github/instructions/self-check.instructions.md` for the types of changes present:

- **C# modified?** → Verify types exist (grep), namespace imports correct.
- **Tests modified?** → Verify naming convention, fixture usage.
- **Localization keys added?** → Verify both `.resx` and `.sv.resx` updated, key pattern correct.
- **Cache keys added?** → Verify in `CacheKeys.cs`, tags defined, invalidation wired.
- **Endpoints modified?** → Verify registration in `WebApplicationExtensions`.
- **Entities/DbContext modified?** → Flag if migration is needed.
- **DI registration modified?** → Verify service registered correctly.

If findings exist, fix them now.

---

## Phase 5b: README updates

If the changes include **new user-facing features, architecture changes, or capability additions**, update the relevant README sections to reflect reality:

1. **`README.md`** (root) — Check these sections:
   - **Feature bullets** (Asset Management, Access Control & Sharing, Security) — add/update bullets for new capabilities.
   - **Architecture table** — update project purpose descriptions if scope changed.
   - **Quick-start / configuration** — update if new settings, env vars, or setup steps were added.
2. **`tests/E2E/README.md`** — Update if new E2E test pages, helpers, or config were added.
3. **`CONTRIBUTING.md`** — Update if conventions, build steps, or project structure changed.
4. **`CLAUDE.md` + registered standards docs** (e.g. `docs/STYLEGUIDE.md`) — if the diff changes architecture (how layers communicate, what a core component is, hosting/topology, an error contract), search these for sentences the change invalidates. **An architecture change is not complete until the standard's affected prose is corrected in the same commit** — a standard describing a dead architecture teaches drift, and its stale examples stop compiling while authors fill the vacuum with divergent idioms.

**Rules:**
- Only update sections that are directly affected — do not rewrite unrelated content.
- Keep bullet style and tone consistent with existing text.
- If unsure whether a change warrants a README update, err on the side of updating — stale docs are worse than minor over-documentation.
- Do NOT update READMEs for internal refactors, test-only changes, or bug fixes that don't change user-visible behavior.

---

## Phase 6: Build

```powershell
dotnet build --configuration Release --no-incremental
```

- Must pass with **zero warnings** (CI enforces this).
- **Read warnings only from a build that re-ran the analyzers — i.e. a non-incremental one.**
  A static-analysis verdict is only valid on a build that actually executed the analyzers.
  An incremental build *skips* analyzers for any project it considers up-to-date, so a
  filtered "no warnings on my files" result from an incremental rebuild is meaningless — it
  can silently hide warnings that shipped in the changed code (e.g. an earlier full build
  reported them, a later incremental one emitted nothing, and the warnings were mistaken for
  absent). Hence `--no-incremental` on the verification pass, and likewise on any re-check
  after iterative fixes: the *last* build you trust for "zero warnings" must be non-incremental.
- **HIGH/CRITICAL NuGet advisories fail the build here** — `Directory.Build.props`
  promotes `NU1903` (high) and `NU1904` (critical) to errors via `WarningsAsErrors`,
  so a vulnerable package breaks `dotnet build` before Phase 6b's scan even runs.
  Fix by upgrading the package (see Phase 6b). Moderate/low advisories stay warnings
  and are caught by the Phase 6b dependency scan, which CI fails on at any severity.
- If build fails, diagnose the errors, fix them, and re-run until clean.
- After fixing, return to Phase 2 for any newly modified files.

---

## Phase 6b: Vulnerability scans (mirror the CI security gates)

CI fails on dependency and container vulnerabilities. Run the same gates locally **before pushing** so a red pipeline is caught here, not on the remote. `.github/workflows/ci.yml` is the source of truth — keep these commands in sync with it.

### Dependency scan (applications) — run every time
The `security-audit` job runs on every push:
```powershell
dotnet list package --vulnerable --include-transitive
```
- **Fails the gate** if the output contains `has the following vulnerable packages`.
- Fix by upgrading the offending package (or a transitive pin) to a non-vulnerable version. A transitive vuln with no direct reference is pinned via a top-level `<PackageReference>` at the fixed version.

### Container scan (containers) — run before a `main` push, or when images/deps changed
The `docker-build` and `infra-image-scan` jobs run **on push to `main` only**, and Trivy fails the build on fixable `CRITICAL`/`HIGH` findings. Because building three multi-stage images is slow, run this when it can matter: pushing to `main`, or any change to a `Dockerfile*`, a base-image tag, or a `*.csproj`/dependency.

Build the three images CI builds, then scan each with **the exact CI flags**:
```bash
docker build -t assethub-api:scan       -f docker/Dockerfile          .
docker build -t assethub-worker:scan    -f docker/Dockerfile.Worker   .
docker build -t assethub-rabbitmq:scan  -f docker/Dockerfile.RabbitMQ docker/
```
Trivy is **not** installed locally by default. Prefer a local `trivy` binary if present; otherwise run the pinned Trivy container against a saved image tar (portable on Windows — no docker-socket mount needed):
```bash
# CI uses trivy v0.69.3 with: --severity CRITICAL,HIGH --ignore-unfixed --vuln-type os,library --exit-code 1
for img in assethub-api:scan assethub-worker:scan assethub-rabbitmq:scan; do
  docker save "$img" -o "trivy-${img%%:*}.tar"
  docker run --rm -v "$PWD:/work" aquasecurity/trivy:0.69.3 image \
    --input "/work/trivy-${img%%:*}.tar" \
    --severity CRITICAL,HIGH --ignore-unfixed --pkg-types os,library --exit-code 1 \
    || echo "::TRIVY FAIL:: $img"
  rm -f "trivy-${img%%:*}.tar"
done
```
(If a local `trivy` is on PATH: `trivy image --severity CRITICAL,HIGH --ignore-unfixed --pkg-types os,library --exit-code 1 <img>`. `--pkg-types` is the current flag; older Trivy uses the `--vuln-type` alias the CI action passes.)

**Triage — match CI exactly:**
- Only **fixable** `CRITICAL`/`HIGH` block (CI sets `ignore-unfixed: true`); unfixed advisories are reported but do not fail.
- A base-image CVE is fixed by bumping the pinned base-image digest/tag in the Dockerfile to a patched release; a library CVE, by upgrading the package.
- If Docker isn't running, or this is a non-`main` branch with no image/dependency changes, the container scan may be **skipped** — note it in the summary; never report it as passed when it didn't run.

---

## Phase 7: SonarCloud analysis

Static analysis of every changed file against the project's SonarCloud rules
(org `oskarc`, project key `oskarc_AssetHub`, configured via
`.vscode/settings.json` → `sonarlint.connectedMode.project`). SonarCloud and
SonarQube share the rule engine and MCP tool names, so the same
`analyze_file_list` / `toggle_automatic_analysis` guidance in
`.github/instructions/sonarqube_mcp.instructions.md` applies verbatim.

### File list

Build the list before choosing a path:

- Every path reported by `git status --porcelain` plus deletions that could
  leave dangling references.
- Exclude: generated files (`*.Designer.cs`, `*.g.cs`,
  `AssetHubDbContextModelSnapshot.cs`), binary assets, `.resx` files (Sonar
  rules don't apply), markdown in `.claude/skills/` (skill docs are not
  code).

### Pick the path that matches available tooling

Check which path is live and take **one**:

1. **Sonar MCP server available** — if the session exposes
   `analyze_file_list`, `toggle_automatic_analysis`, or
   `search_my_sonarqube_projects`:
   1. Call `toggle_automatic_analysis` (off) — if present.
   2. Call `analyze_file_list` on the file list.
   3. Group issues by severity and rule.
   4. Apply the triage rules below.
   5. Do **not** call `search_sonar_issues_in_projects` to verify fixes —
      the MCP guidance says the server may not reflect updates yet.
   6. Re-run `analyze_file_list` on just the fixed files for a local re-check.
   7. Call `toggle_automatic_analysis` (on) on the way out.
   8. Record final counts per severity for the summary.

2. **SonarLint VSCode only (connected mode)** — no MCP, but SonarLint has
   been analysing files as they were saved and publishing issues to the
   VSCode **Problems** panel. Claude cannot read that panel, so:
   1. List the file list to the user and ask: *"Any unresolved SonarLint
      issues in the Problems panel for these files? Paste the rule ids
      (e.g., csharpsquid:S1125) and I'll triage."*
   2. If the user reports issues, apply the triage rules below and fix
      in-place.
   3. If the user confirms the panel is clean for the listed files,
      record `SonarCloud analysis: clean via SonarLint (user-confirmed)`
      in the summary and proceed.
   4. Do **not** attempt to read `~/.sonarlint` cache or VSCode state — it
      is not a documented export format.

3. **Neither available, fallback CLI** — only if the user explicitly opts
   in. Requires `SONAR_TOKEN` env var and `dotnet-sonarscanner` installed:
   ```
   dotnet sonarscanner begin /k:"oskarc_AssetHub" /o:"oskarc" \
     /d:sonar.host.url="https://sonarcloud.io" \
     /d:sonar.login="$SONAR_TOKEN"
   dotnet build --configuration Release
   dotnet sonarscanner end /d:sonar.login="$SONAR_TOKEN"
   ```
   This performs a full branch analysis and publishes to SonarCloud — it's
   not a dry run. Ask the user before running.

4. **Nothing works, last resort** — record
   `SonarCloud analysis: SKIPPED (no scanner available)` in the summary
   and flag it to the user as a coverage gap. Do not block the commit on
   this alone if everything else passed, but do include a post-push
   reminder to check
   `https://sonarcloud.io/project/overview?id=oskarc_AssetHub` once CI has
   published analysis.

### Triage rules (shared across paths)

- **BLOCKER / CRITICAL** — fix before continuing. Real bugs, security
  flaws, contract violations.
- **MAJOR** — fix if local and contained; flag to the user if it would
  need broader refactoring.
- **MINOR / INFO** — fix only if trivial. Report the rest so the user can
  decide.
- **False positives** — do not mark "won't fix" or silence rules without
  the user's explicit OK. Report suspected false positives and move on.
- **New files from fixes** — add them to the file list and re-analyze.
- After any fix, return to Phase 6 (Build) to confirm compilation, then
  resume from here.

---

## Phase 8: Tests

```powershell
dotnet test --configuration Release --no-build
```

- All tests must pass.
- If tests fail:
  1. Read the failing test and the code it tests.
  2. Determine if the test or the production code is wrong.
  3. Fix the root cause (prefer fixing production code if the test expectation is correct).
  4. Re-run `dotnet build --configuration Release` then `dotnet test --configuration Release --no-build`.
  5. Repeat until all tests pass.
- After fixing, return to Phase 2 for any newly modified files. Any new source edits must also be re-analyzed in Phase 7 before reaching Phase 9.

---

## Phase 9: Commit & push

### Compose the commit message

Follow **Conventional Commits** with a **why-focused** body:

```
<type>(<scope>): <short summary in imperative mood>

<WHY this change was made — the motivation, problem, or user need>

- <bullet points of notable implementation decisions if needed>
```

**Rules:**
- **Subject line**: imperative mood, ≤72 chars, no period at the end.
- **Type**: `feat`, `fix`, `refactor`, `style`, `test`, `docs`, `chore`, `perf`, `ci`.
- **Scope**: the affected domain area (e.g., `assets`, `collections`, `auth`, `ui`, `worker`, `infra`).
- **Body**: explain **why** the change was made, not what. The diff shows what; the message explains the motivation.
  - Bad: "Added a null check to GetByIdAsync"
  - Good: "Prevent 500 errors when clients request a deleted asset that is still referenced in collection views"
- **Do NOT include**: file lists, line numbers, generated boilerplate.
- **Co-authored-by**: if the user has a name/email configured in git, do not add an AI co-author trailer.

### Multi-scope changes

If changes span multiple unrelated concerns, ask the user whether to:
1. Commit as a single commit with the broadest scope.
2. Split into multiple focused commits (preferred when changes are truly independent).

### Execute

```powershell
git add -A
git commit -m "<message>"
git push
```

- If push fails due to remote changes, run `git pull --rebase` and re-push.
- If rebase has conflicts, report them to the user — do not auto-resolve merge conflicts.

---

## Abort conditions

Stop the entire flow and report to the user if:
- Build fails after 3 fix attempts on the same error.
- SonarCloud BLOCKER/CRITICAL findings after 3 fix attempts on the same rule.
- Tests fail after 3 fix attempts on the same test.
- A vulnerable dependency or fixable `CRITICAL`/`HIGH` container CVE (Phase 6b) can't be resolved by a safe upgrade / base-image bump — surface it; don't push a change CI will reject.
- A security finding cannot be resolved without architectural changes.
- Merge conflicts arise during push that require manual resolution.

---

## Summary

After successful push, report:
- Branch and remote pushed to.
- Commit hash and message.
- Count of files changed, insertions, deletions (`git diff --stat HEAD~1`).
- SonarCloud finding counts per severity, or the fallback status used (`clean via SonarLint`, `SKIPPED`, etc.) with reason.
- Vulnerability-scan result (Phase 6b): dependency scan pass/fail; container scan pass/fail **or SKIPPED with reason** (Docker down, non-`main` branch with no image/dependency change). Never report a scan that didn't run as passed.
- Any warnings or notes from the review phases.
