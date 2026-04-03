# Copilot Processing Log

## Request
Add upload error details modal — a MudDialog with a table showing per-file error descriptions.

## Action Plan

### Phase 1: Create UploadErrorsDialog component
- [x] Create `src/AssetHub.Ui/Components/UploadErrorsDialog.razor`

### Phase 2: Update AssetUpload.razor
- [x] Open dialog from `FinishProcessing()` when failures exist
- [x] Add clickable error icon to re-open dialog
- [x] Remove per-file `Feedback.ShowError()` during upload loop

### Phase 3: Add localization strings
- [x] `CommonResource.resx` + `CommonResource.sv.resx`

### Phase 4: Verify
- [x] Build compiles with zero warnings

## Summary
Created `UploadErrorsDialog.razor` — a MudDialog with a MudSimpleTable showing file name, size, and error description for each failed upload. Updated `AssetUpload.razor` to:
- Open the dialog automatically when uploads finish with failures (both after processing poll and after immediate failures)
- Replace per-file snackbar toasts with a single dialog at batch completion
- Make the error icon clickable to re-open the dialog
- Keep failed uploads visible in the list until the user closes the dialog
- Added English and Swedish localization strings
- [x] `src/AssetHub.Worker/Consumers/ProcessImageConsumer.cs`
- [x] `src/AssetHub.Worker/Consumers/ProcessVideoConsumer.cs`

### Phase 6: Fix build errors ✅
- [x] Add `using Wolverine.ErrorHandling;` to Worker/Program.cs (for `OnException<T>()`)
- [x] Add `using Wolverine.ErrorHandling;` to Api/Program.cs

### Phase 7: Build & Test verification ✅
- [x] `dotnet build --configuration Release` — Build succeeded (0 errors)
- [x] ZipBuildServiceAuditTests — 2/2 passed
- [x] Integration tests (Endpoints + EdgeCases) — 112/112 passed

## Summary

MassTransit → Wolverine migration is complete. All source files, tests, and infrastructure are updated.

**Files modified this session:**
- `src/AssetHub.Infrastructure/Services/ZipBuildService.cs` — IPublishEndpoint → IMessageBus
- `src/AssetHub.Application/Services/IZipBuildService.cs` — Doc comments updated
- `src/AssetHub.Infrastructure/Services/ImageProcessingService.cs` — Doc comment updated
- `src/AssetHub.Infrastructure/Services/VideoProcessingService.cs` — Doc comment updated
- `src/AssetHub.Api/Program.cs` — Added `using Wolverine.ErrorHandling;`
- `src/AssetHub.Worker/Program.cs` — Added `using Wolverine.ErrorHandling;`
- `tests/AssetHub.Tests/Fixtures/CustomWebApplicationFactory.cs` — MassTransit test harness → Wolverine test isolation
- `tests/AssetHub.Tests/Services/ZipBuildServiceAuditTests.cs` — Mock<IPublishEndpoint> → Mock<IMessageBus>

**Files deleted:**
- 5 old MassTransit consumer files (Api/Consumers + Worker/Consumers)

**Not changed (intentional):**
- `GetLocalizedEventType` in `Home.razor` and `AdminAuditTab.razor` — uses `AdminLoc` (different resource), not a simple wrapper
- `FormatTimeAgo` in `Home.razor` — contains custom logic, not a localization delegation
- `AssetDisplayHelpers` static methods — kept as-is for backward compatibility

---

## Session: Upload Failure Fix (2026-04-02)

### Request
User reported 4 uploaded files all failed processing.

### Root Cause Analysis
1. **Wolverine routing misconfiguration**: Both API and Worker had `opts.ApplicationAssembly = typeof(Program).Assembly` which only discovers local handlers. Without explicit routing, `ProcessImageCommand` published by API had "No routes can be determined" — messages silently dropped.
2. **RabbitMQ healthcheck failure (PID limit)**: `pids: 150` too low — `rabbitmq-diagnostics` spawns Erlang VM needing ~28 scheduler threads, causing SIGABRT.
3. **RabbitMQ healthcheck failure (.erlang.cookie permissions)**: After PID increase to 256, `cap_drop: ALL` removed `DAC_READ_SEARCH` — root healthcheck process couldn't read `.erlang.cookie` (0400, owned by rabbitmq UID 999).

### Fixes Applied

#### Phase 1: Wolverine message routing ✅
- `src/AssetHub.Api/Program.cs` — Added `PublishMessage<T>().ToRabbitQueue()` for ProcessImageCommand, ProcessVideoCommand, BuildZipCommand + `ListenToRabbitQueue()` for asset-processing-completed/failed
- `src/AssetHub.Worker/Program.cs` — Added `ListenToRabbitQueue()` for process-image/video/build-zip + `PublishMessage<T>().ToRabbitQueue()` for AssetProcessingCompletedEvent/FailedEvent

#### Phase 2: RabbitMQ PID limit ✅
- `docker/docker-compose.yml` — RabbitMQ `pids: 150` → `pids: 256`
- `docker/docker-compose.prod.yml` — Same fix

#### Phase 3: RabbitMQ .erlang.cookie permission ✅
- `docker/docker-compose.yml` — Added `DAC_READ_SEARCH` to RabbitMQ `cap_add`
- `docker/docker-compose.prod.yml` — Same fix

### Verification
- RabbitMQ: healthy, `rabbit@... is fully booted and running`
- Worker: listening on `process-image`, `process-video`, `build-zip` queues
- API: publishing to Worker queues, listening on `asset-processing-completed`, `asset-processing-failed`
- All containers running and healthy
- User can now retry uploads to verify end-to-end processing
