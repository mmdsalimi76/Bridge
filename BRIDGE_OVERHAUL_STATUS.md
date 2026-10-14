# Bridge Overview Overhaul — Status & Handoff Document

> **Purpose:** Single source of truth for the approved Overview/Dashboard overhaul and the current remediation plan. Update the Progress Log after each milestone.
>
> Last updated: 2026-10-10. Current status: the client builds cleanly and 69 Core tests pass. A broader quality-improvement program is in progress; “10/10” is treated as a target for measurable release gates, not a claim of perfection.

## 1. Goal

Rebuild the Overview tab of Bridge (Avalonia .NET 10 VPS manager) per the approved plan:

1. **Security first** — SSH host-key verification, shell-injection-proof quoting, credential handling, cancellation.
2. **Performance** — collapse the Overview refresh from up to 7 sequential SSH round trips into exactly 1.
3. **Overview UX** — split the 700-line god-VM into sub-VMs, reusable card controls, threshold coloring, ANSI log pane.
4. **Architecture** — whole-app DI + a shared SSH session coordinator (single owner of connection lifecycle).
5. **Insight** — metric sparklines/deltas, richer server signal (failed units, top processes, disk mounts), exportable Markdown diagnostics report.

Constraints from AGENTS.md: interfaces govern all IO, no UI types in Core, no shell interpolation without
quoting, async everywhere with CancellationToken, tests for Core logic.

## 2. Plan status

| Phase | Scope | Status |
|---|---|---|
| 1 Security | `IHostKeyVerifier`/`HostKeyPolicy` (fail-closed) + `JsonKnownHostsStore` + `HostKeyTrustWindow`; `SshService` HostKeyReceived + 30s keep-alive + `ConnectionLost` + fingerprint; shared `ShellQuote`/`RemotePath` applied to health probes, Docker compose path, inventory grep; cancellation threading | DONE (Core builds 0/0) |
| 2 One-round-trip probe | `OverviewProbe`/`IOverviewProbe` + `OverviewProbeRequest`/`OverviewSnapshot`; one remote script with `##SECTION` markers (METRICS/DOCKER/SERVICES/FAILED/PROCESSES/TOPPROC/DISK/DOCKERDF/HEALTH + `##EXIT=`); per-section degradation; `RemoteCommandCount == 1` asserted | DONE + tested |
| 4 DI + session | `SessionCoordinator`/`ISessionCoordinator` in Core (owns connect/disconnect/credential check, selection versioning kills VPS-switch races); `BridgeServiceCollection.AddBridgeApplication()`; `App.axaml.cs` provider lifecycle; `MainWindowViewModel` constructor-injected | DONE |
| 3 Overview UX | Sub-VMs `ConnectionPanel`/`Metrics`/`Workloads`/`Attention`/`ActivityFeed` behind `DashboardViewModel` facade (30s PeriodicTimer, refresh gate); `MetricCard`/`StatusCard`/`Sparkline`/`BrushForLevelConverter`; rebuilt `DashboardView.axaml` + code-behind (clipboard, save-report picker, scroll-respecting follow logs, Enter-to-connect); ANSI log pane | DONE — blocker fixed, see §7 [S3.1]-[S3.2] |
| 5 Sparklines + diagnostics | `MetricHistoryStore` (bounded 60-sample window + deltas), `AttentionItemBuilder` (85/90/95 thresholds, `MetricLevels`), `DiagnosticsReportBuilder` (Markdown, host redacted by default); export wiring | DONE + tested — compiles and passes, see §7 [S3.2]-[S3.3] |

## 3. The single blocker (RESOLVED — see §7 [S3.1])

**File:** `src/VpsManager.Client/Views/Controls/AnsiColorizingTransformer.cs` line 20
**Error:** `CS1061: 'CurrentDocument' does not exist` (AvaloniaEdit 12.0.0)

**Verified cause:** grep of `~/.nuget/packages/avalonia.avaloniaedit/12.0.0/lib/net10.0/AvaloniaEdit.dll`
metadata shows NO `CurrentDocument` member anywhere. Upstream source
(`AvaloniaUI/AvaloniaEdit Rendering/DocumentColorizingTransformer.cs`) exposes
`protected ITextRunConstructionContext CurrentContext` instead; the base `Colorize()` sets it
before calling `ColorizeLine(DocumentLine)`. `CurrentContext.Document` is a `TextDocument`,
which has `GetText(int offset, int length)`.

**Exact fix (one line):**
```csharp
// WAS:  var text = this.CurrentDocument.GetText(line);
// NOW:  var text = CurrentContext.Document.GetText(line.Offset, line.Length);
```
`DocumentLine` has `Offset` and `Length` (confirmed in upstream base class source).
Call sites: only `TerminalViewer.axaml.cs` constructs `new AnsiColorizingTransformer()` and
attaches/detaches it on `_editor.TextArea.TextView.LineTransformers` — no constructor change needed.

## 4. Verification plan (in order)

1. Apply the one-line fix (sed/perl via shell — the desktop edit tools are NOT in this build's tool list).
2. `dotnet build VpsManager.slnx -v q --nologo` → must end 0 errors. Capture real exit status
   (redirect to log, then `echo $?` — do not trust piped grep's exit code).
3. `dotnet test VpsManager.slnx` → all Core suites green (OverviewProbeTests, OverviewLogicTests,
   OverviewHealthCheckTests, SystemMetricsServiceTests, RuntimeInventoryServiceTests, SecurityAndConfigTests,
   PatchAndSideloadServiceTests, CoreIntegrationAndServiceTests).
4. Runtime smoke test: `dotnet run --project src/VpsManager.Desktop` — Avalonia reflection `{Binding}`
   errors only appear at runtime. Check startup without crash, then kill the test process.
   (test-vps sandbox E2E only if Docker is up; otherwise note as remaining for the user.)
5. Update this file's Progress Log after EACH step above.

## 5. Gotchas for the next agent

- **Tooling:** this Freebuff build exposes `run_terminal_command`/`read_files`/`code_search` but NOT
  `write_file`/`str_replace`. Use Git Bash heredocs (`cat > file << 'EOF'`) and `sed -i` for edits.
- **cwd:** `run_terminal_command` defaults to the project root `/d/Projects/Vps Manager` (= `D:\Projects\Vps Manager`).
  Paths with spaces must be quoted. Git Bash flags need double-slash form (`taskkill //PID`).
- **Session instability:** earlier turns were cut by provider errors mid-implementation; re-verify on-disk
  state before assuming anything (the one-line fix survived 4 failed turns un-applied).
- **Avalonia XAML** compiles at build time but `{Binding}` paths resolve at runtime — green build ≠ working UI.
- **Test data:** 98.75% RAM usage classifies as **Critical** (critical threshold 95), not Warning.
- **Known prior issues all addressed in code:** duplicate connection lifecycle (now SessionCoordinator),
  7-round-trip refresh (now 1), no host-key check (now fail-closed + trust prompt), double-quoted shell
  interpolation in health probes (now `ShellQuote.Quote`), regex-unsafe project paths
  (now `ShellQuote.EscapeExtendedRegex`).

## 6. Key files

```
src/VpsManager.Core/
  Security/            IHostKeyVerifier, HostKeyPolicy, JsonKnownHostsStore, HostKeyInfo, HostKeyVerificationException
  Helpers/             ShellQuote, RemotePath, AnsiColorParser (ParseSpans/ContainsAnsiCodes)
  Services/            SshService (+host key, keep-alive, ConnectionLost), SystemMetricsService (shared static builders),
                       RuntimeInventoryService (shared static builders/parsers), DockerService, PatchService, DatabaseService
  Services/Session/    SessionCoordinator : ISessionCoordinator
  Services/Overview/   OverviewProbe, OverviewModels, AttentionItems, WorkloadFilter, MetricHistoryStore, DiagnosticsReportBuilder
src/VpsManager.Client/
  BridgeServiceCollection.cs, App.axaml.cs (DI provider lifecycle)
  ViewModels/          MainWindowViewModel (injected), DashboardViewModel (facade)
  ViewModels/Dashboard/ ConnectionPanelViewModel, MetricsViewModel, WorkloadsViewModel, AttentionViewModel, ActivityFeedViewModel
  Controls/            Sparkline, MetricCard, StatusCard
  Views/               DashboardView.axaml(+cs), HostKeyTrustWindow.axaml(+cs), MainWindow.axaml (retargeted bindings)
  Views/Controls/      TerminalViewer.axaml(+cs), AnsiColorizingTransformer.cs  ← was the blocker, now fixed
tests/VpsManager.Core.Tests/  OverviewProbeTests, OverviewLogicTests (+8 more suites)
```

## 7. Progress Log

- **[S0]** 2026-10-09 — Architecture review is complete. The app has a strong default design: shared session ownership, fail-closed host-key logic, and shell quoting helpers are aligned with secure and maintainable patterns.
- **[S1]** 2026-10-09 — We validated the real project state rather than trusting the stale status document. The concrete correctness issues were narrow: Docker inventory parsing compatibility and some project-state/entry-point confusion, not a broad architectural failure.
- **[S2]** 2026-10-09 — The actual runnable app entry point is the Avalonia client project. The desktop placeholder project is not the path to validate during smoke testing.
- **[S3]** 2026-10-09 — The parser fix was implemented and verified in [src/VpsManager.Core/Services/RuntimeInventoryService.cs](D:/Projects/Vps%20Manager/src/VpsManager.Core/Services/RuntimeInventoryService.cs): the service now accepts both JSON and tab-delimited Docker inventory output.
- **[S4]** 2026-10-09 — Theme hardening was completed in [src/VpsManager.Client/Styles/ThemeManager.cs](D:/Projects/Vps%20Manager/src/VpsManager.Client/Styles/ThemeManager.cs), [src/VpsManager.Client/App.axaml.cs](D:/Projects/Vps%20Manager/src/VpsManager.Client/App.axaml.cs), and the main window/trust dialog XAML. This removes repeated hard-coded dark overrides and aligns startup behavior with the OS preference.
- **[S5]** 2026-10-09 — Navigation/state cleanup was improved in [src/VpsManager.Client/ViewModels/MainWindowViewModel.cs](D:/Projects/Vps%20Manager/src/VpsManager.Client/ViewModels/MainWindowViewModel.cs) by a typed navigation model and cleaner profile-sync logic. This reduces stringly-typed coupling and keeps selected profile state consistent across tabs.
- **[S6]** 2026-10-09 — Verification pass: `dotnet build src/VpsManager.Client/VpsManager.Client.csproj -v q --nologo` succeeded with 0 warnings and 0 errors. `dotnet test VpsManager.slnx --nologo --verbosity minimal` also passed: 64/64 tests green.
- **[S7]** 2026-10-09 — The current state is stable and evidence-based: the app builds, the Core test suite passes, and the real client entry point starts successfully. Remaining work is polish and live SSH/integration validation, not emergency repair.
- **[S8]** 2026-10-10 — Theme detection was hardening for cross-platform behavior: Windows still reads the registry, macOS checks `defaults read -g AppleInterfaceStyle`, Linux checks GNOME color scheme settings, and unsupported platforms fall back to a safe light theme instead of forcing dark. Build and tests remain green after this change.
- **[S9]** 2026-10-10 — Session/credential review fixes: Settings connection tests now use an isolated SSH service from the same host-key-verification factory, so testing does not replace the app-wide session; connection attempts are serialized and superseded attempts are cancelled in `SessionCoordinator`; profile credential key names now use the shared helper; persistence capability is exposed by the coordinator instead of passing the credential store into the connection-panel view model. Added a focused coordinator concurrency test. Client build passed with 0 warnings/errors; tests passed 65/65.

## 8. Quality Improvement Plan — Release Gates

“10/10” is not objectively provable for a whole application. The practical target is to eliminate known high-severity defects, meet explicit quality gates, and document remaining risks with evidence. Work proceeds in this order; progress and validation are added here after each milestone.

| Step | Work | Exit criteria | Status |
|---|---|---|---|
| 1. Secret handling | Trace SSH and database secrets end-to-end, including backups and scheduled jobs; eliminate secret-bearing command-line arguments and persisted script literals where feasible; establish platform-native persistence and redaction expectations. | No credential appears in remote process arguments, generated scripts, logs, or reports; tests prove command/script output does not contain the secret. Any durable remote credential requirement has an explicitly approved storage design. | IN PROGRESS — one-off PostgreSQL backup/restore and scheduled backup passed on the test VPS using SSH stdin, container tmpfs, and `systemd-creds`; the scheduled script contains no password. MySQL/MongoDB and stronger disk-at-rest protection remain unvalidated. |
| 2. Shared session ownership | Keep connection tests isolated; serialize/supersede concurrent switches and disconnects; ensure tabs route work only through the app's shared transport and active profile. | Deterministic concurrency tests; no direct view-model connection lifecycle bypass; stale operations cannot report connected to a different profile. | MOSTLY COMPLETE — connection test isolation and connect serialization are implemented; broader operation/profile-switch race coverage remains. |
| 3. Dependency composition | Move feature-service construction out of view models into the composition root; remove avoidable concrete dependencies and service-locator patterns. | DI graph validates at startup/test; view models do not instantiate domain services; each service has one deliberate lifetime. | IN PROGRESS — the deployment pipeline's build and health-check services are now required constructor dependencies and are registered through DI; a whole-app construction/lifetime audit remains. |
| 4. Reliability and lifecycle | Review fire-and-forget startup, cancellation, event subscriptions, exception surfacing, and app shutdown disposal. | No unobserved task failures in app-owned work; cancellable operations cleanly stop; services and subscriptions have defined lifetime/disposal. | IN PROGRESS — removed `async void` from profile-change handling and added surfaced error reporting around startup/profile reload; remaining async operations still need a full audit. |
| 5. UI/UX and accessibility | Exercise profile management, password entry, host-key trust, reconnect/recovery, busy states, keyboard access, screen-reader labels, and light/dark/system appearance. | Critical workflows are keyboard operable, errors are actionable, sensitive fields behave intentionally, and layouts are checked at representative sizes/themes. | PENDING |
| 6. Correctness/performance tests | Add targeted unit and integration tests for security boundaries, multi-profile session changes, errors, and measured refresh/network behavior. | All automated suites pass; security-sensitive behavior has regression coverage; latency/round-trip claims are backed by tests or measurements. | IN PROGRESS — 69 Core tests pass, including secret-handling and session-concurrency coverage; broader coverage and live workflow tests remain. |
| 7. Production-like validation | Run client smoke tests and test-VPS workflows for host-key trust, deploy/backup/restore, cancellation, dropped connections, and terminal output. | Reproducible run notes and outcomes; unavailable infrastructure is recorded as a release blocker, not inferred as passing. | IN PROGRESS — test-VPS PostgreSQL backup, restore, encrypted scheduled credential provisioning, cron execution, and generated-script redaction passed; other workflows and database engines remain. |
| 8. Release readiness | Consolidate architecture/security findings, operational docs, known limitations, and release checklist. | No unresolved Critical/High findings; documented backup/restore and credential recovery; clean release build and smoke/E2E evidence. | PENDING |

### Progress for this plan

- **[Q0]** 2026-10-10 — Plan recorded. Baseline from this work segment: client build succeeded with 0 warnings/errors; 65/65 Core tests passed. Workspace already contained unrelated deletions and untracked files before this plan; those are out of scope and will not be reverted.
- **[Q1]** 2026-10-10 — Secret-handling trace found a high-priority issue in `DatabaseService`: MySQL/MongoDB passwords are interpolated into remote client command arguments, and scheduled backup scripts persist those password literals on the VPS. Shell quoting mitigates injection, but not process-list or at-rest exposure. No remediation that changes scheduled-backup behavior will be applied without choosing an acceptable secret-storage strategy.
- **[Q2]** 2026-10-10 — Reviewing dependency composition next. DI cleanup can proceed independently of the scheduled-credential storage decision.
- **[Q3]** 2026-10-10 — Removed `PatchService` and `DeploymentPipelineService` construction from `DeployViewModel`; the pipeline is now registered and constructor-injected from the client composition root.
- **[Q4]** 2026-10-10 — The scheduled database-backup design was approved: use VPS-side `systemd-creds`, validate encryption/decryption support before installing the cron job, and keep decrypted credential configuration only in the database container's `/dev/shm`.
- **[Q5]** 2026-10-10 — Added SSH command stdin streaming. One-off MySQL, MongoDB, and PostgreSQL backups now pass client credential configuration over the SSH channel and write it to a mode-0600 temporary file in container tmpfs; restore uses the same tmpfs approach. Scheduled credentials are sent as stdin to `systemd-creds encrypt`, validated with a decrypt-to-`/dev/null` round trip, and never embedded in the generated cron script. Cron expressions are constrained to five safe fields; generated scripts are transferred through stdin instead of a shell heredoc. Added regression coverage for secret redaction, schedule encryption output, and cron injection rejection.
- **[Q6]** 2026-10-10 — Composed deployment build and health-check services through DI instead of constructing them inside `DeploymentPipelineService`. Replaced the `async void` profile-change handler with an error-reporting task boundary and included disconnect/profile synchronization in server-switch error handling. Client build succeeded with 0 warnings/errors; all 69 solution tests passed; the Avalonia client remained running through a five-second startup smoke check. Broader test-VPS workflows and the async lifecycle audit remained outstanding at that milestone.
- **[Q7]** 2026-10-10 — Ran the actual `SshService` and `DatabaseService` against the running Ubuntu 24.04 test VPS and a disposable PostgreSQL 16 container: SSH stdin round-trip passed; one-off backup produced a valid gzip archive; restore recreated a dropped table and its test row; scheduled credential encryption and script redaction passed; cron executed the generated job and recorded a successful, valid backup. Test artifacts, cron entry/script, encrypted credential, history changes, and disposable database container were cleaned up. `systemd-creds` emitted its expected warning that the test VPS host credential key is not on encrypted media; this sandbox therefore validates functionality, not disk-theft resistance. MySQL/MongoDB and other production workflows remain untested.
