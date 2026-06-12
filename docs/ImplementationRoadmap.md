# BP-MIFIR Implementation Roadmap

Date: 2026-06-12
Status: Draft implementation plan

This roadmap translates the target architecture into an incremental engineering plan. The governing principle is that every step must leave the application in a useful and runnable state. No step should require a long rewrite before users can benefit from the work already completed.

## Delivery Principles

- Preserve the current manual converter until a replacement path is proven.
- Use a strangler pattern: extract, wrap, and replace behaviour in small increments.
- Maintain at least one useful operating mode at all times.
- Prefer deterministic rules and test fixtures over manual inspection.
- Keep source files, generated files, and evidence files separate.
- Never introduce CySEC live submission until validation, archive, retry, and operator visibility are in place.
- Treat every regulatory workflow action as replayable and auditable.
- Use feature flags/configuration to enable new capabilities gradually.

## Operating Modes

These modes allow partial execution throughout the project:

| Mode | Available after | Purpose |
| --- | --- | --- |
| Manual Converter Mode | Current app / Step 1 | User selects a source XML and saves ESMA output manually. |
| Archived Manual Mode | Step 5 | Manual conversion also writes original/output/warnings/hash evidence to local archive. |
| Local Automation Mode | Step 8 | Drop folder detects files, validates, converts, archives, and reports status without CySEC submission. |
| Submission Simulation Mode | Step 12 | System submits to a local/mock CySEC target and processes mock feedback. |
| CySEC Assisted Mode | Step 14 | System prepares and stages CySEC-ready files, with operator approval for upload. |
| CySEC Live Mode | Step 15 | System submits and polls CySEC automatically under configured guardrails. |
| Compliance Mirror Mode | Step 18 | Accepted/rejected evidence packages are mirrored to M365 when enabled. |

The app must always display the current mode clearly.

## Step 0: Baseline and Branch Control

### Objective

Establish a reliable engineering baseline before changing the application structure.

### Engineering Work

- Confirm repository state is clean or intentionally documented.
- Create a dedicated development branch for the platform rewrite.
- Record current release package, version, and known behaviour.
- Confirm target .NET runtime and Visual Studio/SDK version.
- Preserve current `MainWindow.xaml.cs` behaviour as the baseline.
- Add `docs/ImplementationRoadmap.md` to the project documentation.

### Useful State After Step

No application behaviour changes. The current converter remains usable.

### Acceptance Checks

- `dotnet build BPMifir.sln` succeeds.
- Existing manual DBRegHub-to-ESMA flow still launches.
- Current source, docs, and release artefacts are identifiable.

## Step 1: Regression Fixture Harness for Current Converter

### Objective

Create a safety net before refactoring.

### Engineering Work

- Add a test project, for example `BPMifir.Tests`.
- Add synthetic DBRegHub fixture files:
  - valid new transaction,
  - cancellation transaction,
  - missing mandatory cancellation fields,
  - missing mandatory new-transaction fields,
  - price `NOAP`,
  - price `PNDG`,
  - natural-person buyer/seller,
  - branch-country fallback.
- Add a test helper that runs conversion without UI interaction.
- If direct conversion is not yet extractable, begin with serialization/deserialization tests and small mapper helper tests.
- Add golden output XML snapshots for stable representative cases.

### Useful State After Step

The current app still works manually, and future changes have regression coverage.

### Acceptance Checks

- Test project compiles.
- Fixtures contain no real client data.
- At least the core DB model deserialisation and ESMA serialization paths are covered.

## Step 2: Extract Conversion Orchestrator Behind Current UI

### Objective

Move conversion logic out of WPF event handlers without changing user workflow.

### Engineering Work

- Add `BPMifir.Core` class library.
- Add `ConversionRequest`, `ConversionResult`, `ConversionWarning`, and `ConversionError` models.
- Add `IReportConverter` interface.
- Move the non-UI logic from `LoadTransactionsBtn_Click` into a conversion service.
- Keep `MainWindow.xaml.cs` as a thin orchestrator:
  - collect UI fields,
  - select source file,
  - call converter,
  - show warnings,
  - save output file.
- Keep generated serializer models available to the converter.

### Useful State After Step

The app behaves the same to the user, but conversion can now run from tests and future automation.

### Acceptance Checks

- Manual UI flow still works.
- Existing warnings popup still works.
- Tests can invoke conversion service without WPF.
- No source or generated ESMA output shape changes except intentional bug fixes.

## Step 3: Introduce Canonical Report Model

### Objective

Create an internal representation so Deutsche Boerse and UnaVista can feed the same ESMA generation path.

### Engineering Work

- Add canonical models:
  - `CanonicalReportBatch`,
  - `CanonicalTransaction`,
  - `CanonicalParty`,
  - `CanonicalInstrument`,
  - `CanonicalPrice`,
  - `CanonicalQuantity`,
  - `CanonicalRegulatoryFlags`,
  - `CanonicalSourceReference`.
- Map Deutsche Boerse DBRegHub into canonical form.
- Map canonical form into ESMA output.
- Keep a compatibility path so current UI still converts DBRegHub files.
- Record mapping version in conversion result.

### Useful State After Step

Manual DBRegHub conversion still works, but the app is now ready for UnaVista without duplicating ESMA mapping.

### Acceptance Checks

- Golden output remains equivalent for existing DB fixtures.
- Canonical JSON can be emitted for diagnostics in test mode.
- Mapping version appears in test output.

## Step 4: Deterministic Format Detection

### Objective

Detect supported source files safely before parsing.

### Engineering Work

- Add `BPMifir.Adapters` abstractions:
  - `IInputFormatDetector`,
  - `IInputAdapter`,
  - `DetectedFormat`.
- Add Deutsche Boerse detector:
  - XML root `reportFile`,
  - namespace `http://deutsche-boerse.com/DBRegHub`.
- Add unsupported XML and unsupported CSV outcomes.
- Read a bounded prefix before full parse.
- Ignore file extension for trust decisions.
- Add UI display of detected format after file selection.

### Useful State After Step

Manual UI conversion can tell the user whether a file is supported before attempting conversion.

### Acceptance Checks

- DBRegHub fixture is detected.
- Unknown XML is rejected cleanly.
- Malformed XML is rejected cleanly.
- Large/unsafe files fail with controlled errors.

## Step 5: Local Archive for Manual Conversions

### Objective

Add evidence preservation before unattended automation.

### Engineering Work

- Add `BPMifir.Storage` class library.
- Add archive root config, defaulting to a user-selectable local folder for now.
- Add archive package writer:
  - original input,
  - generated ESMA output,
  - warnings/errors,
  - manifest JSON,
  - SHA-256 hashes.
- Add `BatchId` generation.
- Add archive result path to success message.
- Do not require SQLite yet.

### Useful State After Step

Manual users get a useful local evidence package for every conversion.

### Acceptance Checks

- Successful manual conversion creates archive folder.
- Skipped/rejected transactions are recorded.
- Manifest contains source hash, output hash, app version, mapping version, timestamp.
- Existing save-to-file workflow still works.

## Step 6: Preflight and Validation Service

### Objective

Centralise validation so UI, worker, and tests use the same rules.

### Engineering Work

- Add `IValidationRule` and `ValidationResult`.
- Move existing mandatory-field checks into `BPMifir.Core.Validation`.
- Separate:
  - source parse errors,
  - format detection errors,
  - mandatory-field errors,
  - schema errors,
  - warning-only conditions.
- Add validation severity:
  - `Info`,
  - `Warning`,
  - `TransactionRejected`,
  - `BatchRejected`,
  - `SystemError`.
- Add validation rule version.

### Useful State After Step

Manual conversion has clearer validation output and a reusable validation engine.

### Acceptance Checks

- Current skip-warning behaviour remains.
- Validation result can be serialized to archive.
- Tests verify that non-mandatory missing fields do not reject transactions.

## Step 7: SQLite Workflow Store

### Objective

Persist batch state without changing the user-facing operating model.

### Engineering Work

- Add SQLite database under a configurable data path.
- Add explicit migration scripts.
- Add tables:
  - `batches`,
  - `batch_events`,
  - `files`,
  - `validation_results`,
  - `config_versions`.
- On manual conversion, write:
  - `Received`,
  - `Identified`,
  - `Validated`,
  - `Transformed`,
  - `Archived`.
- Add a small UI status/history view if feasible.

### Useful State After Step

Even manual conversions now have durable state and can be audited locally.

### Acceptance Checks

- Database is created if missing.
- Migration table records schema version.
- Manual conversion creates batch rows and event rows.
- Deleting the database does not destroy archive evidence.

## Step 8: Drop Folder Intake in Desktop App

### Objective

Add automation without introducing a Windows Service yet.

### Engineering Work

- Add configured drop folders:
  - `incoming`,
  - `processing`,
  - `quarantine`,
  - `archive`.
- Add a desktop UI command: `Scan Drop Folder Now`.
- Implement safe file pickup:
  - skip files still being copied,
  - move to processing atomically,
  - hash before processing,
  - quarantine unsupported/malformed files.
- Process files through the same converter and archive path.
- Show results in UI.

### Useful State After Step

Operators can drop files into a folder and process them in batches without selecting files one by one.

### Acceptance Checks

- Manual file selection still works.
- Scan command processes multiple DBRegHub files.
- Unsupported files move to quarantine with reason.
- No CySEC submission occurs.

## Step 9: Background Worker as Console/Interactive Process

### Objective

Prove unattended processing before installing a Windows Service.

### Engineering Work

- Add `BPMifir.Worker` as a .NET Worker project.
- Initially run it as a console process.
- Reuse the same configuration, converter, validation, archive, and workflow store.
- Implement periodic scanning.
- Add graceful shutdown.
- Add structured logging.

### Useful State After Step

A user can run the worker manually and get unattended local conversion/archive.

### Acceptance Checks

- Worker processes drop-folder files.
- Desktop UI can still inspect database/archive after worker processing.
- Worker recovers safely after being stopped during idle periods.

## Step 10: Desktop Operations Console

### Objective

Make automated processing visible and controllable.

### Engineering Work

- Add queue/status screen:
  - batch ID,
  - source file,
  - detected format,
  - state,
  - accepted/rejected/skipped count,
  - last error,
  - archive path.
- Add filters for:
  - active,
  - warnings,
  - rejected,
  - quarantined,
  - accepted.
- Add actions:
  - open archive folder,
  - copy warnings,
  - reprocess quarantined file after configuration change,
  - export batch evidence.

### Useful State After Step

The app becomes a usable local operations dashboard for manual and drop-folder processing.

### Acceptance Checks

- UI can show manually processed and worker-processed batches.
- No direct database edits are required by users.
- No regulatory data payloads are shown in logs unless intentionally opened from archive.

## Step 11: Windows Service Installation Mode

### Objective

Turn the worker into a real unattended local service.

### Engineering Work

- Add Windows Service hosting with `Microsoft.Extensions.Hosting.WindowsServices`.
- Add service account guidance.
- Add service recovery options:
  - restart on failure,
  - delayed start.
- Add event log source.
- Add local health check file or status endpoint for UI.
- Keep console mode for debugging.

### Useful State After Step

The system can process files unattended after machine restart.

### Acceptance Checks

- Service starts and stops cleanly.
- Drop-folder files are processed while UI is closed.
- UI can read status while service is running.
- Manual UI conversion still works if service is disabled.

## Step 12: CySEC Submission Mock

### Objective

Build submission semantics without risking live filings.

### Engineering Work

- Add `ICySecSubmissionClient`.
- Add `FileSystemCySecClient` mock:
  - submitted files copied to a local mock `outbox`,
  - feedback read from local mock `feedback`.
- Add submission states:
  - `ReadyToSubmit`,
  - `Submitted`,
  - `AwaitingFeedback`,
  - `Accepted`,
  - `Rejected`.
- Add mock feedback parser.
- Add idempotency controls:
  - `BizMsgIdr`,
  - batch ID,
  - output hash.

### Useful State After Step

The full filing lifecycle can be demonstrated locally without CySEC credentials.

### Acceptance Checks

- Mock accepted feedback moves batch to `Accepted`.
- Mock rejected feedback opens rejection case.
- Restart after mock submission resumes at `AwaitingFeedback`, not re-transform.
- Archive includes submitted file and feedback file.

## Step 13: Rejection Case Workflow

### Objective

Handle rejection outcomes before enabling live submission.

### Engineering Work

- Add rejection classification model:
  - source data defect,
  - mapping bug,
  - schema/config issue,
  - duplicate/reference issue,
  - transient system issue,
  - partial acceptance.
- Add remediation case table.
- Add UI for rejection details, owner, notes, status.
- Add linked correction batch concept.
- Prevent in-place mutation of rejected archive.

### Useful State After Step

The system can receive feedback, preserve evidence, and guide remediation locally.

### Acceptance Checks

- Rejected mock feedback creates immutable rejected batch.
- Operator can classify and add notes.
- Correction creates a new linked batch.
- Rejected archive is not overwritten.

## Step 14: CySEC Assisted Submission

### Objective

Introduce real CySEC connectivity under operator approval.

### Engineering Work

- Add real FTP/FTPS/SFTP client according to CySEC TRS documentation.
- Add connection test screen.
- Add credential storage using DPAPI, Windows Credential Manager, or Certificate Store as appropriate.
- Add `Stage for CySEC` and `Submit with Approval` modes.
- Add upload receipt metadata.
- Keep automatic live submission disabled by default.

### Useful State After Step

Operators can prepare and submit real CySEC files with explicit approval and complete archive evidence.

### Acceptance Checks

- Connection test distinguishes authentication, permission, network, and path errors.
- Approved submission uploads file once.
- Feedback polling can be run manually.
- Archive records CySEC endpoint config version and upload metadata.

## Step 15: CySEC Live Automation

### Objective

Enable unattended live submission after assisted mode has proven safe.

### Engineering Work

- Add configuration flag: `LiveSubmissionEnabled`.
- Add safe-state checks before submission.
- Add retry/backoff for upload and feedback polling.
- Add deferred update behaviour during active submission.
- Add alerting for:
  - failed upload,
  - delayed feedback,
  - rejection,
  - repeated transient errors.
- Add maximum retry policy and manual intervention threshold.

### Useful State After Step

The system can run end-to-end unattended for supported formats and configured CySEC channel.

### Acceptance Checks

- Live mode cannot be enabled accidentally.
- Upload retries are idempotent.
- Feedback is never deleted before local archive and hash.
- Unknown remote state leads to manual action, not blind resubmission.

## Step 16: UnaVista Adapter

### Objective

Add second source format without disturbing the DBRegHub path.

### Engineering Work

- Obtain official UnaVista sample/specification.
- Add `BPMifir.Adapters.UnaVista`.
- Add detector for UnaVista XML/CSV/XLSX as confirmed.
- Map UnaVista fields to canonical model.
- Add UnaVista fixtures and golden outputs.
- Add source-format-specific validation rules.

### Useful State After Step

The same UI, worker, archive, validation, and CySEC process now supports both DBRegHub and UnaVista inputs.

### Acceptance Checks

- DBRegHub tests still pass.
- UnaVista valid fixture converts to ESMA.
- UnaVista unsupported variant quarantines cleanly.
- Archive records source format and adapter version.

## Step 17: ESMA and Source Schema Validation

### Objective

Add formal schema validation once authoritative XSDs are available.

### Engineering Work

- Add `schemas/` folders:
  - `dbreghub`,
  - `unavista`,
  - `esma`,
  - `cysec-feedback`.
- Add schema version metadata.
- Add XML schema validation service.
- Disable DTD and external entity resolution.
- Validate generated ESMA XML before submission.
- Store validation reports in archive.

### Useful State After Step

The system catches schema issues before CySEC submission and provides evidence.

### Acceptance Checks

- Invalid schema fixture fails before submission.
- Valid fixtures pass.
- ESMA output is schema-validated where schema package is available.
- Schema version appears in batch manifest.

## Step 18: M365 Compliance Mirror and Notifications

### Objective

Add M365 value without making it a critical dependency.

### Engineering Work

- Add optional Microsoft Graph integration.
- Add SharePoint archive mirror for finalised batches.
- Add metadata columns:
  - batch ID,
  - report date,
  - source format,
  - status,
  - app version,
  - source hash,
  - submitted hash.
- Add Teams/email notification adapter if approved.
- Add retry queue for mirror failures.
- Ensure local archive remains authoritative.

### Useful State After Step

Compliance users get off-machine evidence and notifications, while local filing continues if M365 is unavailable.

### Acceptance Checks

- Accepted/rejected batch mirrors to SharePoint.
- M365 outage does not block local CySEC workflow.
- Mirror retry succeeds later.
- No report payload is sent to AI or unapproved services.

## Step 19: Standalone Installer and Guarded Update/Remove

### Objective

Package the product professionally for deployment and maintenance.

### Engineering Work

- Add WiX Toolset installer project or signed bootstrapper.
- Install:
  - desktop UI,
  - worker service,
  - schema files,
  - default config,
  - ProgramData folder structure.
- Add guarded update:
  - signature check,
  - database/config backup,
  - service stop/start,
  - migration execution,
  - update evidence.
- Add guarded uninstall:
  - remove binaries/service,
  - preserve archive/data/config/logs by default,
  - explicit `/remove-data` for full deletion.
- Add repair path.

### Useful State After Step

The product can be installed, updated, repaired, and removed without developer intervention.

### Acceptance Checks

- Fresh install works on clean test machine.
- Update preserves archive and database.
- Default uninstall leaves regulatory evidence.
- Repair restores service and binaries.
- Silent install/uninstall commands work for future Intune packaging.

## Step 20: Security Hardening

### Objective

Close common local-app and XML/file-processing risks.

### Engineering Work

- Enforce bounded file size.
- Enforce safe XML parser settings.
- Ensure logs never contain full report payloads.
- Add ACL setup for ProgramData folders.
- Add secrets storage abstraction.
- Add dependency vulnerability check.
- Add SBOM generation.
- Add code signing to release flow.
- Add operational health checks.

### Useful State After Step

The app is safer for regulated data and easier for IT/compliance to approve.

### Acceptance Checks

- XXE test fixture is blocked.
- Oversized file is quarantined.
- Logs contain hashes/IDs, not client personal data payloads.
- Release package has SBOM and vulnerability review output.

## Step 21: DORA, DPIA, and Operational Evidence Pack

### Objective

Prepare the artefacts needed for regulated-entity review.

### Engineering Work

- Add deployment/admin guide.
- Add operator guide.
- Add incident runbook.
- Add backup/restore runbook.
- Add third-party dependency register.
- Add data-flow and data-retention summary.
- Add test evidence pack:
  - crash recovery,
  - FTP outage,
  - feedback delay,
  - rejected report,
  - restore drill,
  - update rollback.

### Useful State After Step

The client can perform internal compliance, IT, and operational readiness review.

### Acceptance Checks

- Documentation exists and matches product behaviour.
- Restore drill passes.
- Update rollback drill passes or limitations are documented.
- Client can identify all data locations and third-party dependencies.

## Step 22: Pilot Release

### Objective

Run the system in controlled operation before production.

### Engineering Work

- Deploy to pilot workstation or filing server.
- Run with synthetic and controlled real samples.
- Start in Local Automation or Submission Simulation mode.
- Move to CySEC Assisted mode after approval.
- Collect pilot issues.
- Add regression tests for defects found.
- Freeze scope for production candidate.

### Useful State After Step

The system is useful in daily operations with a controlled risk profile.

### Acceptance Checks

- Pilot users can process files with minimal support.
- Archive evidence is complete.
- No critical workflow defects remain open.
- CySEC test or assisted submission evidence is reviewed.

## Step 23: Production Release Candidate

### Objective

Prepare a controlled production deployment.

### Engineering Work

- Tag release candidate.
- Build signed installer.
- Run full automated test suite.
- Run representative end-to-end UAT.
- Verify installer/update/uninstall guardrails.
- Confirm operational runbooks.
- Confirm backup/restore.
- Confirm final config.
- Confirm support and patch process.

### Useful State After Step

Client can accept or reject the release candidate for production use.

### Acceptance Checks

- Release manifest is complete.
- Production config is approved.
- UAT sign-off recorded.
- Support path agreed.
- Rollback package retained.

## Step 24: Production Go-Live and Hypercare

### Objective

Deploy production with monitoring and rapid response.

### Engineering Work

- Install production package.
- Verify service status.
- Verify drop folder and archive paths.
- Verify CySEC connection in approved mode.
- Process first production batch with operator oversight.
- Confirm archive and feedback handling.
- Monitor for agreed hypercare period.

### Useful State After Step

The system is live and operationally supported.

### Acceptance Checks

- First production batch evidence package is complete.
- Accepted/rejected feedback path is proven.
- Support handover completed.
- Open issues are triaged into warranty, change request, or backlog.

## Backlog Items After Production V1

These should not block production V1 unless explicitly required:

- AI-assisted rejection summaries.
- Advanced dashboards.
- Intune deployment packaging.
- Full SharePoint intake mode.
- Multi-tenant support.
- Additional source formats.
- Regulatory change monitoring.
- Automated reconciliation against trade source systems.
- Advanced role-based UI with Entra ID.

## Recommended Commercial Milestones

| Milestone | Covers | Useful app state |
| --- | --- | --- |
| M1: Stabilised converter | Steps 0-3 | Current manual converter with testable core. |
| M2: Local archive pilot | Steps 4-8 | Manual/drop-folder conversion with evidence archive. |
| M3: Worker and workflow | Steps 9-13 | Unattended local workflow with mock submission and rejection handling. |
| M4: CySEC assisted/live | Steps 14-17 | Real submission path and schema validation. |
| M5: Packaging and compliance | Steps 18-21 | Installer, M365 mirror, hardening, compliance evidence. |
| M6: Pilot and go-live | Steps 22-24 | Production-ready deployment and hypercare. |

## Minimum Useful Cut Lines

If budget or time is constrained, stop at one of these cut lines:

### Cut Line A: EUR 20k Pilot

- Steps 0-8.
- No CySEC live submission.
- No UnaVista unless sample format is trivial.
- Useful for local DBRegHub conversion, drop-folder processing, and evidence archiving.

### Cut Line B: Controlled Filing Pilot

- Steps 0-14.
- CySEC assisted submission only.
- Useful for real filing preparation with operator approval.

### Cut Line C: Production V1

- Steps 0-21.
- Full live workflow, installer, archive, and compliance evidence.

### Cut Line D: Production Rollout

- Steps 0-24.
- Pilot, go-live, and hypercare included.
