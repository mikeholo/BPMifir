# Target Architecture Decision Record

Date: 2026-06-11
Status: Product direction accepted for planning

These notes record the agreed product redesign choices for BP-MIFIR. They are product and architecture guidance for implementation planning, not legal or regulatory advice.

For the incremental engineering sequence that preserves partial usefulness at each stage, see `docs/ImplementationRoadmap.md`.

## Goal

Redesign the current manual Deutsche Boerse-to-ESMA desktop converter into a resilient MiFIR filing workflow system that can:

- Accept incoming Deutsche Boerse DBRegHub or UnaVista files from the UI or a watched drop folder.
- Detect supported input formats automatically.
- Validate, translate, and submit ESMA transaction reports to CySEC TRS through the configured FTP channel.
- Poll for asynchronous CySEC feedback.
- Archive original, submitted, and feedback files with enough evidence to prove what happened.
- Minimise user workload while preserving deterministic controls and auditability.

## Core Product Choice

Use a hybrid local-first architecture:

- A Windows background service performs unattended intake, validation, conversion, submission, feedback polling, retry, and archival.
- A lightweight desktop UI provides queue visibility, manual import, configuration, warnings, remediation, and operational status.
- M365 is used only where it adds low-cost value: controlled intake, compliance archive mirror, notifications, update distribution, and evidence discovery.
- The regulatory submission path remains deterministic and rule-based. AI is not allowed to create or modify regulatory filing data automatically.

This choice is preferred over a pure desktop app, pure M365/Power Automate implementation, cloud-native Azure build, or commercial ARM platform because it best balances resilience, cost, privacy, deterministic control, and low operational burden.

## Core Components

The target codebase should be split into these modules:

- `BPMifir.Core`: canonical workflow, validation contracts, conversion interfaces, and deterministic business rules.
- `BPMifir.Adapters.DeutscheBoerse`: DBRegHub format detection, parsing, and canonical mapping.
- `BPMifir.Adapters.UnaVista`: UnaVista format detection, parsing, and canonical mapping.
- `BPMifir.Esma`: ESMA schema model, serializer, schema validation, and output naming.
- `BPMifir.CySec`: CySEC TRS FTP submission and feedback polling.
- `BPMifir.Worker`: Windows Service for drop-folder scanning and durable workflow execution.
- `BPMifir.Desktop`: WPF user interface for monitoring, manual filing, remediation, and configuration.
- `BPMifir.Storage`: SQLite workflow store, archive manager, hash manifest, and recovery logic.

The current WPF converter logic should be extracted from `MainWindow.xaml.cs` into these modules before adding UnaVista or unattended filing.

## Implementation Technology Stack

Use the Microsoft/.NET stack already present in the project unless there is a specific reason to introduce another runtime. This keeps deployment simple for Windows users, avoids new hosting costs, and fits regulated-entity support expectations.

### Language and Runtime

- Primary language: C#.
- Runtime: .NET 10 for the application codebase, matching the current `net10.0-windows` project target.
- UI framework: WPF for the desktop operator console.
- Background processing: .NET Worker Service hosted as a Windows Service.
- Build mode for release: self-contained `win-x64` publish so client workstations do not need a preinstalled .NET runtime.

If a client standardises on a different supported .NET LTS runtime, the runtime can be revisited during implementation, but the architecture should remain .NET-first.

### Solution Structure

Target solution layout:

```text
BPMifir.sln
  src/
    BPMifir.Core/
    BPMifir.Adapters.DeutscheBoerse/
    BPMifir.Adapters.UnaVista/
    BPMifir.Esma/
    BPMifir.CySec/
    BPMifir.Storage/
    BPMifir.Worker/
    BPMifir.Desktop/
  tests/
    BPMifir.Core.Tests/
    BPMifir.Adapter.Tests/
    BPMifir.Esma.Tests/
    BPMifir.Workflow.Tests/
    BPMifir.Integration.Tests/
  schemas/
    dbreghub/
    unavista/
    esma/
    cysec-feedback/
  docs/
  tools/
    release/
    diagnostics/
```

Migration should be incremental. First extract the existing converter into `BPMifir.Core`, `BPMifir.Adapters.DeutscheBoerse`, and `BPMifir.Esma`; then add the worker, storage, CySEC submission, and UnaVista support.

### Recommended NuGet Libraries

Use permissively licensed libraries only. Every dependency must be recorded in the third-party register with licence, version, purpose, and update owner.

| Area | Recommended choice | Reason |
| --- | --- | --- |
| Hosting and DI | `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection` | Native .NET worker/service pattern. |
| Windows Service hosting | `Microsoft.Extensions.Hosting.WindowsServices` | Runs the worker as a managed Windows Service. |
| Configuration | `Microsoft.Extensions.Configuration.Json`, environment providers | Standard .NET configuration stack. |
| Logging | `Microsoft.Extensions.Logging` plus Serilog file/EventLog sinks | Structured logs, rolling local files, Windows Event Log integration. |
| SQLite | `Microsoft.Data.Sqlite` | Lightweight local durable store with no server dependency. |
| SQL access | Direct SQL repository layer; optionally Dapper | Explicit SQL is easier to audit than hidden ORM behaviour. |
| XML parsing | `System.Xml`, `XmlReader`, `XmlSerializer`, `XmlSchemaSet` | Existing model compatibility and deterministic schema validation. |
| JSON manifests | `System.Text.Json` with source-generated serializers where useful | Built-in, fast, deterministic enough for manifests. |
| Hashing/signatures | `System.Security.Cryptography` | SHA-256 manifests and tamper-evident hash chains. |
| CSV input | `Sylvan.Data.Csv` if UnaVista CSV is required | Fast parser with permissive licensing; avoids spreadsheet automation. |
| XLSX input | `DocumentFormat.OpenXml` only if official UnaVista input requires XLSX | Reads XLSX without Excel installed. Prefer CSV/XML where possible. |
| FTP/FTPS | `FluentFTP` | Mature .NET FTP/FTPS client; use only if CySEC TRS uses FTP/FTPS. |
| SFTP | `SSH.NET` | Use only if CySEC or a source provider requires SFTP. |
| M365/SharePoint | Microsoft Graph SDK with `Azure.Identity` | Supported path for SharePoint mirroring and Teams/email notification. |
| UI MVVM | `CommunityToolkit.Mvvm` | Lightweight MVVM without large framework overhead. |
| Test framework | xUnit, `Microsoft.NET.Test.Sdk`, FluentAssertions | Familiar .NET test stack. |
| Golden file tests | `Verify.Xunit` or controlled in-house XML comparison helpers | Prevent accidental XML output changes. |

Avoid:

- Excel COM automation.
- Database servers.
- Message brokers.
- Cloud workflow engines in the critical filing path.
- GPL or unclear-licence dependencies unless legal approves.
- AI libraries in deterministic conversion/submission code.

### Local Data and Runtime Paths

Default machine-wide paths:

```text
C:\ProgramData\BP-MIFIR\
  config\
  data\
    workflow.db
  drop\
    incoming\
    processing\
    quarantine\
  archive\
  logs\
  schemas\
  temp\
  updates\
```

Rules:

- `archive`, `data`, and `config` must be protected with Windows ACLs.
- The volume should be encrypted with BitLocker.
- Temporary files are written under `temp` and deleted after successful state transition.
- The service runs under a dedicated least-privilege local or domain service account.
- Operators do not need write access to internal state tables; they interact through the UI.

### SQLite Store

SQLite is the local workflow database. It should hold state, not primary report payloads.

Core tables:

- `batches`: one row per filing batch.
- `batch_events`: append-only event stream for state transitions and decisions.
- `files`: file paths, role, size, SHA-256, and immutable/finalised flag.
- `submissions`: CySEC submission metadata and retry counters.
- `feedback`: raw feedback path, parsed status, and linked rejected transactions.
- `remediation_cases`: rejection classification, owner, status, and linked correction batch.
- `config_versions`: snapshot of endpoint, schema, mapping, and validation versions.
- `schema_migrations`: applied local database migrations.

Use explicit SQL migration scripts checked into source control. Do not rely on automatic destructive migrations.

### XML and Schema Validation

Schema files should be checked into `schemas/` and versioned in source control:

- Deutsche Boerse DBRegHub XSD.
- UnaVista input schema or published field specification.
- ESMA `auth.016.001.01` schema package accepted by CySEC.
- CySEC feedback schema or parser specification.

Rules:

- XML readers must disable DTD processing and external entity resolution.
- Validate source file schema before mapping when an authoritative schema is available.
- Validate generated ESMA XML before submission.
- Store validation results in the batch archive.
- Generated C# model classes must record source XSD version and generator version.

### Format Detection

Format detection must be deterministic and ordered:

1. Read only a bounded prefix of the file for safety.
2. Detect XML root name and namespace.
3. Detect known CSV headers if non-XML.
4. Detect compressed containers only if explicitly supported.
5. Reject unknown formats to quarantine with a clear reason.

Detection must never infer a supported format from file extension alone.

### CySEC TRS Connectivity

Create a `ICySecSubmissionClient` abstraction with concrete implementations selected by configuration:

- FTP/FTPS via `FluentFTP`.
- SFTP via `SSH.NET` if required.
- Local filesystem mock for testing.

Required behaviours:

- Atomic upload pattern where supported: upload temporary name, verify size/hash if possible, then rename.
- Retry with exponential backoff for network failures.
- Distinguish authentication failure, permission failure, unavailable server, timeout, and malformed response.
- Persist every attempted upload and poll event before continuing.
- Do not delete remote feedback until it is locally archived and hashed.

### M365 Integration

Use Microsoft Graph only for non-critical-path functions:

- Mirror finalised archive packages to a restricted SharePoint library.
- Apply metadata columns such as batch ID, report date, source format, status, hash, and app version.
- Send Teams/email notifications for accepted, rejected, delayed, or failed filings.
- Optionally read from a SharePoint intake folder and copy files to the local drop folder.

Authentication:

- Prefer certificate-based app registration with least-privilege Graph permissions.
- Use `Sites.Selected` or tightly scoped SharePoint permissions where available.
- Store secrets in Windows Certificate Store or DPAPI-protected local configuration.
- Do not use user mailbox credentials or plain-text secrets.

### Secrets and Credentials

Use this order of preference:

1. Windows Certificate Store for certificates and private keys.
2. DPAPI-protected local secret file bound to machine or service account.
3. Windows Credential Manager where operationally preferred.

Do not store:

- CySEC FTP passwords in JSON config.
- Graph client secrets in source control.
- Report payloads in logs.
- Operator credentials in the app database.

### Logging and Diagnostics

Use structured logs with a strict no-payload policy:

- Log batch ID, state, event type, status, hashes, counts, elapsed time, and error codes.
- Do not log client names, national IDs, dates of birth, transaction payloads, or full XML.
- Write local rolling logs under `C:\ProgramData\BP-MIFIR\logs`.
- Mirror important service-level events to Windows Event Log.
- Store operator-visible diagnostics in the workflow database and archive evidence folders.

Operational logs should support these questions:

- Which files were received?
- Which format was detected?
- Which validation rules failed?
- Which file was submitted?
- When was CySEC feedback received?
- Which user approved a manual action?
- Which version of the app and mapping was used?

### Security Controls

Minimum technical controls:

- Code-signed binaries.
- BitLocker for archive volume.
- Windows ACLs on archive, database, config, and logs.
- Dedicated service account with least privilege.
- TLS/FTPS/SFTP for file transfer where CySEC supports it.
- SHA-256 file hashing for every archived artefact.
- Tamper-evident manifest hash chain.
- No dynamic code execution in mapping rules.
- No macros or Office automation.
- Dependency vulnerability scan before each release.
- SBOM generated for each release.

### Release and Update Tooling

Preferred release flow:

1. Build Release self-contained `win-x64`.
2. Run unit, integration, and golden-file tests.
3. Generate SBOM.
4. Run `dotnet list package --vulnerable`.
5. Sign binaries and installer.
6. Create release manifest with version, commit hash, schema versions, mapping versions, and SHA-256.
7. Publish package to Intune, SharePoint App Installer location, or restricted SharePoint update folder.
8. Keep prior release package available for rollback.

Preferred package:

- MSIX/App Installer for controlled auto-update when the client environment supports it.
- Intune deployment if already available in the M365 tenant.
- Signed self-contained ZIP only as a fallback or emergency patch path.

### Developer Tooling

Use:

- Visual Studio or Rider for WPF and C# development.
- `dotnet` CLI for repeatable builds and tests.
- Git for source control.
- PowerShell release scripts under `tools/release`.
- Local fake CySEC server or filesystem mock for integration tests.
- Test data fixtures under `tests/fixtures`, with synthetic or anonymised data only.

Do not commit:

- Real client reports.
- Real CySEC credentials.
- Production FTP endpoints unless approved as non-secret config.
- Personal data samples.
- Generated release ZIPs.

### Test Strategy

Required test categories:

- Format detection tests for DBRegHub, UnaVista, unknown XML, unknown CSV, malformed file, and oversized file.
- Mapping tests from source file to canonical model.
- ESMA output golden-file tests.
- Schema validation tests.
- Preflight mandatory-field tests.
- Cancellation handling tests.
- Archive manifest and hash-chain tests.
- Crash recovery tests for each workflow state.
- CySEC upload retry and feedback polling tests using mocks.
- Rejection classification tests.
- M365 mirror tests using a mock Graph client.
- Security tests for XML external entity protection and no-payload logging.

Every production bug in mapping or submission should add a regression fixture.

### Dependency and Licence Governance

Maintain `docs/ThirdPartyRegister.md` before production. Each dependency entry must include:

- Package name.
- Version.
- Licence.
- Purpose.
- Data exposure, if any.
- Update owner.
- Vulnerability review date.

Dependencies that transmit data externally require DORA third-party and DPIA review.

## Workflow State Machine

Every filing batch must move through an explicit durable state machine:

```text
Received
Identified
Validated
Transformed
Submitted
AwaitingFeedback
Accepted | Rejected | ManualAction | Retry | Quarantined
```

Rules:

- State transitions are persisted before external side effects where possible.
- External side effects are idempotent using batch ID, business message ID, and file hash.
- The worker resumes from the last durable state after crash, restart, power loss, or network outage.
- The app never silently resubmits a report if it cannot prove whether the prior submission reached CySEC.

## Archive Design

The source of operational truth is a local encrypted archive. M365 is the compliance mirror, not the only record.

### Layer 1: Local Primary Archive

- Stored on the filing workstation or local filing server.
- Protected with BitLocker or equivalent full-volume encryption.
- Restricted by Windows ACLs to filing operators, compliance, and support administrators.
- Contains immutable batch packages once a batch is finalised.
- Used for fast replay, investigation, and disaster recovery.

### Layer 2: M365 Compliance Mirror

- Stored in a restricted SharePoint document library.
- Protected by Microsoft Purview retention labels.
- Accepted filing packages should be labelled as records or regulatory records where the tenant licence supports it.
- Used for off-machine resilience, eDiscovery, retention, management visibility, and compliance evidence.
- M365 location must be reviewed against EU Data Boundary and internal outsourcing/DORA register requirements.

### Layer 3: Offline or Independent Backup

- Periodic encrypted export to removable media, backup appliance, or secure network storage.
- Used when both local workstation and M365 access are unavailable.
- Restore drills must be performed and evidenced.

## Archive Folder Layout

Each batch receives a stable `BatchId` and an immutable folder:

```text
Archive/
  yyyy/
    MM/
      dd/
        BP-MIFIR-yyyyMMdd-nnnnnn/
          00-intake/
            original.xml
            source-detected.json
          01-normalized/
            canonical-report.json
          02-validation/
            input-schema-validation.json
            esma-preflight-validation.json
            warnings.txt
          03-submission/
            submitted-esma.xml
            submission-metadata.json
            cysec-upload-receipt.txt
          04-feedback/
            cysec-feedback-original.xml
            parsed-feedback.json
          05-final/
            acceptance-certificate.json
            rejection-case.json
          06-evidence/
            manifest.json
            hash-chain.json
            app-version.txt
            mapper-version.txt
            config-version.json
            operator-actions.jsonl
```

No file in a finalised batch is overwritten. Corrections create a new batch linked with `CorrectsBatchId`.

## Versioning and Evidence

Each batch manifest must record:

- Batch ID.
- Source format: Deutsche Boerse DBRegHub or UnaVista.
- Source file path at intake.
- Source file SHA-256.
- Submitted ESMA file SHA-256.
- CySEC feedback file SHA-256.
- App version.
- Build commit hash.
- Adapter versions.
- Canonical model version.
- ESMA schema version.
- CySEC endpoint/configuration version.
- Validation rule version.
- Timestamp of each workflow state transition.
- Operator identity for manual actions.
- Machine identity.
- Previous batch manifest hash where applicable.

This makes the archive tamper-evident and reproducible: the firm can prove which software, rules, config, and source file produced the submitted ESMA file and CySEC response.

SharePoint version history is useful but secondary. The app must preserve immutable batch packages and hashes independently of SharePoint versioning.

## Recovery Requirements

Minimum recovery behaviours:

- If the app crashes during intake, the file remains in `incoming` or moves to `processing` with a resumable lock record.
- If it crashes after conversion but before submission, it resumes from `Transformed`.
- If it crashes after upload but before feedback, it resumes from `AwaitingFeedback` and does not resubmit blindly.
- If CySEC FTP is unavailable, retry with exponential backoff and raise an alert after a configured threshold.
- If feedback is delayed, continue polling and escalate after SLA threshold.
- If a file is malformed or unsupported, move it to `Quarantined` with a reason and preserve the original.
- If an accepted batch exists in the local archive but is missing from M365, re-mirror it without altering the local evidence package.
- If local workflow state and archive disagree, the archive manifest is used to reconstruct workflow state and the inconsistency is logged as an operational incident.

## Rejection Handling

Rejected filings are not edited in place. The rejected batch remains immutable and a remediation case is opened.

Rejection classes:

- Source data defect: notify operator/source provider; no automatic data invention.
- Mapping bug: quarantine affected files; block same mapping signature until fixed.
- Schema/config issue: block submissions; raise system incident.
- Duplicate/reference issue: reconcile against prior submissions and CySEC feedback.
- Transient CySEC/system issue: retry only when safe and idempotent.
- Partial acceptance: archive accepted records and isolate rejected records for remediation.

Any resubmission creates a new batch linked to the rejected batch.

## M365 Usage Boundaries

Approved uses:

- SharePoint drop folder for optional intake.
- SharePoint compliance archive mirror.
- Teams/email notifications for accepted, rejected, delayed, or failed filings.
- SharePoint or Intune distribution of signed updates.
- Purview retention, records management, audit, and eDiscovery.

Not approved for the critical path:

- Power Automate as the sole workflow engine for CySEC submission.
- AI-generated filing corrections.
- SharePoint version history as the only audit mechanism.
- Cloud-only archive without local reproducible evidence.

## AI Position

AI can be added only as an assistive, non-authoritative feature:

- Summarise CySEC rejection messages.
- Suggest likely remediation category.
- Draft an operator note.
- Search project documentation or previous rejection cases.

AI must not:

- Change report fields automatically.
- Invent missing mandatory fields.
- Decide whether to submit, cancel, amend, or resubmit.
- Override deterministic validation.

Any AI feature requires DPIA review, EU-region processing where applicable, logging of prompts/outputs where permitted, and a human approval boundary.

## Compliance Mapping

### MiFIR and RTS 22

- Keep deterministic field mappings.
- Validate against source and ESMA schema where schemas are available.
- Preserve Article 15-style evidence for completeness, accuracy checks, and reconciliation.
- Track accepted, rejected, pending, and corrected submissions.

### CySEC TRS

- Treat CySEC FTP submission and feedback as asynchronous external systems.
- Archive both raw feedback and parsed feedback.
- Do not infer acceptance without CySEC evidence.
- Keep endpoint, credential, and sequence configuration versioned.

### GDPR / DPIA / DPRI

- Treat client identifiers, national IDs, names, dates of birth, and transaction data as personal/confidential regulatory data.
- Minimise logs: no full payloads in normal application logs.
- Encrypt local archive and credentials.
- Restrict access by role.
- Document legal basis as regulatory reporting obligation.
- Complete DPIA before production deployment.

### DORA

- ICT risk management: documented architecture, access controls, encryption, logging, backup, and change management.
- Incident handling: classify FTP outage, delayed feedback, rejection spike, schema failure, archive corruption, credential failure, and update failure.
- Resilience testing: run crash recovery, replay, restore, and submission outage drills.
- Third-party risk: maintain register entries for Microsoft 365, CySEC TRS, Deutsche Boerse, UnaVista/LSEG, and any update/signing infrastructure.
- Change control: signed builds, versioned mappings, rollback path, and release evidence.
- Continuity: local archive plus M365 mirror plus offline backup.

## Remote Update and Patch Management

Auto-update is required because MiFIR/CySEC reporting issues can be operationally urgent. The update mechanism must still be controlled: it must not silently change regulatory mappings without version evidence, release notes, rollback capability, and operator visibility.

### Update Principles

- Updates are signed, versioned, and traceable to a source commit.
- The app verifies package signature, release manifest hash, and monotonic version before installation.
- The worker never updates while actively submitting or polling a batch unless the update is marked as an emergency stop-the-line release.
- The workflow database and archive are backed up before applying an update that changes schema, mappings, or workflow state.
- Every update writes an `ApplicationUpdated` event to the local event log and workflow database.
- Every filing batch records the app version, adapter version, schema version, and validation-rule version used to produce it.
- Rollback package and migration notes are retained for every production release.

### Current Product Decision: Standalone Signed MSI or EXE Installer

At this stage, BP-MIFIR should ship as a standalone signed MSI or bootstrapper EXE. This keeps installation independent from Intune availability while still allowing a clean future migration to Intune deployment.

The installer must support:

- Fresh install.
- In-place update.
- Repair.
- Guarded removal.
- Silent install/update for future Intune or scripted deployment.
- Explicit version detection for future update automation.

Recommended installer technology:

- Preferred: WiX Toolset MSI because it is mature, scriptable, auditable, and fits Windows Service installation well.
- Acceptable: Advanced Installer or similar commercial MSI authoring only if licensing is approved.
- Fallback: signed bootstrapper EXE wrapping MSI when prerequisites or richer UX are needed.

Installer payload:

- `BPMifir.Worker` Windows Service.
- `BPMifir.Desktop` WPF UI.
- Shared core libraries and schema files.
- Default configuration templates.
- Local folder creation under `C:\ProgramData\BP-MIFIR`.
- Windows Event Log source registration.
- Optional Start Menu shortcut for the operator UI.

Install location:

```text
C:\Program Files\BP-MIFIR\
```

Data location:

```text
C:\ProgramData\BP-MIFIR\
```

The installer must never place mutable data under `Program Files`.

### Install Guardrails

Fresh install must:

- Require administrator rights.
- Create a dedicated Windows Service.
- Create `C:\ProgramData\BP-MIFIR` folder structure.
- Apply restrictive ACLs to config, data, archive, logs, and update cache.
- Refuse to continue if the archive/data path is on an unencrypted volume unless the operator explicitly accepts a documented exception.
- Validate minimum Windows version.
- Validate sufficient disk space.
- Record installer version and install timestamp.

### Update Guardrails

In-place update must:

- Verify the installer is signed by the trusted publisher.
- Stop the worker service gracefully.
- Refuse update or defer update if the worker is actively uploading to CySEC or downloading feedback, unless an emergency override is supplied.
- Backup SQLite workflow database and local config before migration.
- Run explicit database/config migrations.
- Preserve archive, workflow database, logs, credentials, and config.
- Restart the worker service.
- Run post-update health checks.
- Record `ApplicationUpdated` with old version, new version, package hash, and migration result.

Command-line examples:

```powershell
BP-MIFIR-Setup.exe /install /quiet
BP-MIFIR-Setup.exe /update /quiet
msiexec /i BP-MIFIR.msi /qn
```

### Remove Guardrails

Removal must be guarded because the app holds regulatory evidence.

Default uninstall must:

- Stop and remove the Windows Service.
- Remove application binaries from `Program Files`.
- Leave `C:\ProgramData\BP-MIFIR\archive`, `data`, `config`, and `logs` in place.
- Write an uninstall marker and final service status if possible.
- Warn clearly that regulatory evidence is retained.

Full data removal must be a separate explicit action and should not be the default uninstall path.

Full removal should require:

- Administrator rights.
- Explicit command-line flag, for example `/remove-data`.
- Confirmation that archive has been exported or retained elsewhere.
- Optional typed confirmation in interactive mode.
- Final manifest of what was deleted.

Command-line examples:

```powershell
BP-MIFIR-Setup.exe /uninstall /quiet
BP-MIFIR-Setup.exe /uninstall /remove-data
msiexec /x {PRODUCT-CODE} /qn
```

The normal Control Panel / Apps uninstall path must keep regulatory evidence by default.

### Repair Guardrails

Repair must:

- Reinstall missing binaries.
- Re-register the Windows Service.
- Restore default ACLs.
- Recreate missing non-data folders.
- Preserve database, archive, config, credentials, and logs.
- Validate service start after repair.

### Future Intune Compatibility

The standalone installer must be designed so it can later be wrapped as an Intune Win32 app without redesign:

- Silent install and uninstall commands.
- Stable product code/upgrade code strategy.
- File or registry detection rule.
- Clear return codes.
- No interactive prompts in silent mode.
- No forced reboot unless absolutely required.

### Preferred Path When Intune Is Available: Intune-Deployed Signed MSI or EXE Installer

Use this path when the client M365 tenant includes Microsoft Intune or equivalent endpoint management.

Implementation:

- Package BP-MIFIR as a signed MSI or signed bootstrapper EXE.
- Install both the Windows Service and WPF desktop UI.
- Deploy as an Intune Win32 app.
- Use Intune detection rules based on installed product code and file version.
- Use Intune supersedence for upgrades.
- Use required assignment for filing workstations or filing servers where automatic update is mandatory.
- Use pilot rings before broad deployment:
  - Ring 0: internal/developer machine.
  - Ring 1: test filing workstation.
  - Ring 2: production filing workstation after sign-off.

Advantages:

- Minimal end-user load.
- Strong administrative control.
- Central deployment status and retry handling.
- Better fit for Windows Service installation than per-user app update mechanisms.
- Aligns with DORA change control because deployment evidence is centralised.

Required release artefacts:

- Signed installer.
- SHA-256 hash.
- Release manifest JSON.
- SBOM.
- Dependency vulnerability scan result.
- Mapping/schema change summary.
- Rollback installer.
- Test evidence.

### Fallback Path: SharePoint-Hosted Signed Updater

Use this path when Intune is not available and there is no appetite for additional hosting cost.

Initial installation is manual and administrator-approved. After that, a small local updater component handles controlled updates.

Components:

- `BPMifir.UpdateService`: a local Windows Service installed with the product.
- `release-manifest.json`: hosted in a restricted SharePoint document library or HTTPS/file-share location.
- Release package: signed MSI or signed self-contained package.
- Local update cache: `C:\ProgramData\BP-MIFIR\updates`.

Release manifest fields:

```json
{
  "product": "BP-MIFIR",
  "version": "2.1.0",
  "minimumSupportedVersion": "2.0.0",
  "releaseType": "Normal",
  "publishedUtc": "2026-06-11T00:00:00Z",
  "packageUri": "https://tenant.sharepoint.com/sites/regulatory/.../BP-MIFIR-2.1.0.msi",
  "packageSha256": "...",
  "signatureThumbprint": "...",
  "commit": "...",
  "mappingVersion": "2026.06.11.1",
  "esmaSchemaVersion": "auth.016.001.01-cysec-approved",
  "requiresDatabaseMigration": true,
  "requiresServiceRestart": true,
  "rollbackPackageUri": "...",
  "releaseNotesUri": "..."
}
```

Updater workflow:

1. Poll the release manifest on a configured schedule.
2. Verify manifest signature or trusted hash source.
3. Compare current version with available version.
4. Download package to the local update cache.
5. Verify SHA-256 hash.
6. Verify Authenticode signature and trusted publisher.
7. Check that no batch is in a non-interruptible state.
8. Snapshot workflow database and config.
9. Stop `BPMifir.Worker`.
10. Apply installer silently.
11. Run database migrations.
12. Start `BPMifir.Worker`.
13. Run post-update health checks.
14. Record update event locally and mirror the update evidence to M365.

If any verification fails, the updater must refuse to install and raise an operator alert.

### MSIX/App Installer Path

MSIX with App Installer is a good option for desktop-only or UI-focused distribution. Microsoft App Installer supports update behaviour through the `.appinstaller` file, including checks on launch and background checks. MSIX packages must be signed and trusted on the device.

Use MSIX/App Installer only if packaging tests confirm it correctly supports the required service deployment model in the target Windows environment. If the product includes a Windows Service, the MSI/Intune path is safer and more conventional.

Recommended MSIX settings if used:

- `OnLaunch` update checks for operator UI.
- `AutomaticBackgroundTask` only if acceptable to IT policy.
- `UpdateBlocksActivation=true` for mandatory compliance fixes.
- `ForceUpdateFromAnyVersion=true` only for controlled rollback or emergency recovery.
- HTTPS or SMB-hosted `.appinstaller` file in a restricted M365 or file-share location.

### ClickOnce Position

ClickOnce can update WPF apps and is easy for simple per-user desktop deployments, but it is not the preferred production path for BP-MIFIR because:

- The target design includes a Windows Service.
- Regulatory mapping updates need stricter release evidence and rollback handling.
- Endpoint-management status and DORA change evidence are weaker than Intune or a signed service updater.

ClickOnce may be used only for temporary internal prototypes.

### Emergency Patch Path

For urgent defects where automated deployment is blocked:

- Publish a signed self-contained ZIP or signed installer to restricted SharePoint.
- Include SHA-256 and release notes.
- Require operator/admin confirmation.
- Record manual installation in the update log.
- Reconcile installed version with the workflow database at next launch.

Emergency patches should be converted into the normal update channel as soon as possible.

### Update Safety Around Active Filings

The updater must inspect the workflow state before installing:

- Safe to update: no active batch, `Accepted`, `Rejected`, `Quarantined`, `ManualAction`.
- Usually safe after checkpoint: `Received`, `Identified`, `Validated`, `Transformed`.
- Not safe without explicit emergency flag: `Submitted`, `AwaitingFeedback`, active FTP upload, active feedback download.

If an update is pending during an unsafe state, the updater records `UpdateDeferred` and tries again after the batch reaches a safe state.

### Database and Configuration Migrations

Use explicit, idempotent migration scripts:

- Migrations are versioned in source control.
- Each migration runs in a transaction where possible.
- A database backup is taken before migration.
- Migration result is recorded in `schema_migrations`.
- Failed migration triggers automatic restore and service restart on the prior version where possible.

Configuration migrations must preserve:

- CySEC endpoint settings.
- Credential references.
- Drop-folder paths.
- Archive paths.
- M365 site/library IDs.
- Retention/update policies.

### Rollback

Rollback is allowed only to a release that supports the current workflow database version or has a tested downgrade path.

Rollback process:

1. Stop worker.
2. Snapshot current database/config/logs.
3. Install rollback package.
4. Restore database/config backup if required.
5. Start worker.
6. Run health checks.
7. Record `ApplicationRolledBack`.

For mapping bugs, rollback may not be enough. The remediation plan must identify filings produced by the affected mapping version and flag them for review.

### Update Evidence

Every successful or failed update attempt must produce evidence:

```text
C:\ProgramData\BP-MIFIR\updates\
  2026-06-11_2.1.0\
    release-manifest.json
    package.sha256
    signature-check.json
    pre-update-health.json
    database-backup-info.json
    migration-result.json
    post-update-health.json
    update-log.txt
```

The same evidence should be copied to the M365 compliance mirror.

### Recommended Decision

Use this priority order:

1. Intune-deployed signed MSI/EXE if Intune is available.
2. SharePoint-hosted signed updater service if Intune is not available.
3. MSIX/App Installer only for desktop-only or confirmed-compatible deployments.
4. Signed ZIP/manual installer only for emergency fallback.

Auto-update must not silently change mapping rules without recording the mapping version and release evidence.

## Open Items Before Implementation

- Obtain official UnaVista sample files/specification and confirm expected inbound format variants.
- Obtain CySEC TRS FTP technical guide, folder conventions, naming rules, and feedback schema.
- Confirm accepted ESMA schema namespace/version with CySEC or the reporting operations team.
- Confirm Microsoft 365 licence level for Purview retention labels and records management.
- Confirm whether Microsoft Intune is included in the client's M365 licensing.
- Confirm whether IT permits a dedicated local updater service and silent signed installer execution.
- Confirm whether MSIX/App Installer can support the final service + desktop packaging model in the target environment.
- Confirm retention period required by the firm for MiFIR evidence.
- Confirm whether the filing workstation is always on or whether a small local server/VM is required.
- Confirm backup target and restore-time objective.
