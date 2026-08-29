# Implementation Checklist

## GitHub Pages landing page

- [x] Create a dependency-free, responsive landing page under `docs/`.
- [x] Explain AEC directionality, source-of-truth model, current platform, and installation.
- [x] Add the public roadmap for platform and provider support.
- [x] Add GitHub, MIT licence, citation, and PayPal coffee links.
- [ ] Explicitly enable GitHub Pages and verify its deployed public URL.

Immediate next work: review the local landing page, then decide whether to enable
GitHub Pages as a separate external change.

## v1.2.3 — Security containment

- [x] **P1:** Reject linked path ancestors and repository-contained Codex runtime paths in `status` and `backup`.
- [x] **P1:** Disable Git replacement objects throughout `BackupCommand` validation and commit operations.
- [x] Add focused regression tests for both P1 boundaries.
- [x] Pass build, focused tests, the full Debug suite, and diff validation.
- [ ] Pass formatting after resetting the stale .NET build servers.

Immediate next work: decide whether to reset the stale .NET build servers and retry
formatting; do not begin P2 work.

## Test scope isolation roadmap

- [x] Document the current five-minute full-suite expectation and quiet VSTest output.
- [x] Make focused class-filtered tests the normal per-change workflow.
- [x] Reserve the full Debug and Release suites for pre-push and pre-release regression gates.
- [ ] Classify tests as unit, command integration, filesystem safety, or end-to-end.
- [ ] Add stable xUnit traits or separate test projects for those scopes.
- [ ] Measure test durations and prioritize proven bottlenecks before optimization.
- [ ] Remove process-state serialization only where isolated execution is demonstrated.
- [ ] Preserve full-suite regression coverage while making focused feedback faster.

## Security review implementation backlog

- [ ] **P2:** Stage verified blobs without executing configured clean filters or optional Git integrations.
- [ ] **P2:** Reject linked worktrees and Git metadata outside the selected data repository.
- [ ] **P2:** Prevent fresh-init identity cleanup from deleting concurrently created repository state.
- [ ] **P2:** Close or explicitly constrain pathname-based file validation and mutation races.
- [ ] **P3:** Verify the installed executable digest before generated-uninstaller execution and deletion.
- [ ] **P3:** Create a missing runtime `AGENTS.md` with user-only permissions.

## Documentation and architecture/security review

- [x] Explain why generated Codex `MEMORY.md` is outside AEC ownership.
- [x] Complete a read-only architecture review.
- [x] Complete a read-only security review.
- [x] Report actionable findings without bundling unapproved fixes.

## v1.2.2 — Shared test infrastructure

- [x] Centralize `AecApplication.Run` output capture and its result model.
- [x] Centralize Git-backed test setup through the production isolation boundary.
- [x] Remove duplicated test process launchers and result records.
- [x] Preserve every test and assertion.
- [x] Pass analyzer, Debug, Release, shell, skill, and Native AOT validation.
- [ ] Re-run formatting validation after the Roslyn MSBuild host named-pipe timeout.

## v1.2.1 — Behavior-preserving cleanup

- [x] Consolidate shared compiler settings in `Directory.Build.props`.
- [x] Enable the pinned .NET 10 recommended Roslyn rules and build-time code style.
- [x] Replace repetitive help writes with one newline-safe raw string literal.
- [x] Extract repeated required-option value parsing without changing diagnostics.
- [x] Fix every compiler and analyzer diagnostic without suppression.
- [x] Remove tests only after an exact public-path contract supersedes them.
- [x] Pass focused, full, formatting, shell, skill, and Native AOT validation.

## v1.2 — Installer-generated uninstaller

- [x] Generate `scripts/uninstall-aec.sh` beside the macOS ARM64 installer.
- [x] Embed the exact latest selected executable path so cleanup does not use `PATH`.
- [x] Run runtime `aec uninstall` before removing the executable or helper script.
- [x] Preserve `config.toml` and every AEC data repository.
- [x] Refuse to overwrite a non-AEC helper and preflight source-folder write access.
- [x] Recognize the exact v1.1.0 skill as an upgrade and uninstall predecessor.
- [x] Pass shell, skill, Debug, Release, and macOS ARM64 Native AOT validation.
- [x] Pass an isolated generated-uninstaller install-to-uninstall cycle.

## v1.1 — Explicit AEC runtime uninstall

- [x] Define `aec uninstall [--codex-home ABSOLUTE_PATH]` without `--repo`.
- [x] Preserve every data repository and all runtime `config.toml` bytes.
- [x] Remove only the recognized AEC instruction block and official skill files.
- [x] Preserve unrelated instructions, skills, and extra AEC skill-directory files.
- [x] Fail closed for unmanaged `$aec` references and customized managed files.
- [x] Make cleanup idempotent and resumable after instruction removal.
- [x] Recognize the exact v1.0.0 skill as an upgrade and uninstall predecessor.
- [x] Update version metadata, help, README, and bundled skill guidance.
- [x] Pass skill, Debug, Release, and macOS ARM64 Native AOT validation.

## v1.0 — Publication

- [x] Choose the MIT License and public attribution `@ferrywlto`.
- [x] Choose developer-built Native AOT with initial `osx-arm64` support.
- [x] Add the MIT license, project metadata, and goodwill attribution.
- [x] Validate the licensing increment.
- [x] Commit and push the exact licensing paths.
- [x] Capture the developer-authored `osx-arm64` Native AOT build direction.
- [x] Harden and validate the deterministic Native AOT build artifact.
- [x] Add contained user-local binary installation without shell-profile edits.
- [x] Make reinstall the explicit, idempotent binary upgrade path.
- [x] Add binary-only uninstall guidance that preserves AEC state.
- [x] Document prerequisites, build, install, PATH, upgrade, and uninstall.
- [x] Validate clean install, repeated install, upgrade, and uninstall.
- [x] Warn and validate when a custom install directory must be exposed to Codex through `PATH`.
- [x] Commit and push the exact macOS ARM64 publication paths.
- [x] Dogfood the installed Native AOT binary against the selected AEC repository.
- [x] Migrate the sole pre-release v0.8 skill installation to the v0.9 baseline.
- [x] Document the approved pulled-repository initialization, `apply` routing, and path-confirmation flow.
- [x] Add and dogfood `aec skill upgrade` for future official skill versions.
- [x] Document and test Codex restart troubleshooting for a newly installed AEC CLI.
- [x] Complete v1.0 release validation and private end-to-end dogfooding.
- [x] Prepare the `1.0.0` release source and tag target.
- [ ] Publish v1.0 publicly after the private dogfood period concludes.

## v0.13.1 — Codex executable discovery troubleshooting

- [x] Explain that a running Codex process retains the `PATH` from app startup.
- [x] Keep the executable-discovery restart distinct from skill-only upgrade reload.
- [x] Recognize the byte-exact v0.13.0 skill bundle as an upgrade predecessor.
- [x] Pass full Debug and Release test suites plus macOS ARM64 Native AOT validation.
- [x] Dogfood bootstrap, backup, apply, attachment, and installed-skill upgrade flows.

## v0.13 — Explicit installed-skill upgrade

- [x] Approve the separate binary-install and skill-guidance upgrade flow.
- [x] Add focused failing CLI and fail-closed upgrade tests.
- [x] Recognize only exact official v0.9.0 through v0.12.0 predecessors.
- [x] Preflight both managed files, support safe retry, and remain idempotent.
- [x] Update version metadata, help, README, and bundled `$aec` guidance.
- [x] Pass skill, Debug, Release, and macOS ARM64 Native AOT validation.
- [x] Reinstall the live executable and dogfood the installed-skill upgrade after
  separate approval.

## v0.12 — Command-aligned version reporting

- [x] Replace the exceptional `aec --version` option with the `aec version` command.
- [x] Update release metadata, help, documentation, the bundled skill, and installer probes.
- [x] Pass skill, Debug, Release, and macOS ARM64 Native AOT validation.

## v0.10 — Pulled-repository attachment and path routing

- [x] Approve the pulled-repository flow and `--force-path-change` semantics.
- [x] Capture the pivotal `$aec` skill direction from the user.
- [x] Add focused failing tests for v3/v4 binding, attachment, rebinding, and apply routing.
- [x] Recognize only clean completed repositories with the expected initialization history.
- [x] Attach a matching repository through install and apply without backup or commit.
- [x] Rebind a confirmed moved repository with one canonical-only commit.
- [x] Route `apply` and ChatGPT provider path mismatches back to ordinary `init`.
- [x] Update the `$aec` skill, CLI version, help, and documentation.
- [x] Pass skill, Debug, Release, and macOS ARM64 Native AOT validation.
- [x] Commit and push the exact v0.10 paths.

## v0.11 patch series — Managed Codex configuration values

- [x] Manage only selected root settings and preserve unrelated runtime config.
- [x] Manage `personality`; accept `none`, `friendly`, and `pragmatic`, using
  `none` as the missing-value default.
- [x] Keep `model` and `model_reasoning_effort` outside initial ownership.
- [x] Extend `status` to compare only the managed semantic value without mutation.
- [x] Fail closed on invalid canonical, duplicate, unsupported, or ambiguous values.
- [x] Release the read-only status checkpoint as v0.11.1.
- [x] Extend `apply` to update or warn before inserting managed runtime values.
- [x] Document and release the repository-to-runtime checkpoint as v0.11.2.
- [x] Extend `backup` to capture existing managed values and warn and stop when
  they are missing.
- [x] Document and release the runtime-to-repository checkpoint as v0.11.3.
- [x] Extend `init` to enroll existing values or warn before adding the default.
- [x] Document the two-file initialization history, missing-value default, and
  legacy-history compatibility for v0.11.4.
- [x] Update the bundled skill guidance for the completed v0.11 command set.
- [x] Pass skill, Debug, Release, and macOS ARM64 Native AOT validation.

## v0.9 — Commit-first initialization

- [x] Confirm a missing runtime is an error.
- [x] Fix the second commit subject as `Initialize AEC instructions`.
- [x] Add focused failing lifecycle and baseline-resume tests.
- [x] Commit the exact runtime baseline through the backup flow.
- [x] Reconcile and commit the AEC block only in the canonical source.
- [x] Apply without overwriting runtime changes made after the committed baseline.
- [x] Resume only a recognizable baseline-only partial initialization.
- [x] Reject every other non-empty initialization target.
- [x] Update the `$aec` skill, CLI version, and documentation.
- [x] Pass skill, Debug, and Release validation.
- [x] Commit and push the exact v0.9 engine paths.

## v0.8 — Explicit repository-aware initialization

- [x] Require `--repo` for `init`; remove positional/default-directory semantics.
- [x] Define `aec help` and the missing-`--repo` diagnostic.
- [x] Define absolute repository and ChatGPT paths for managed instructions.
- [x] Authorize direct `dotnet run` migration and a local-only data commit.
- [x] Add focused failing CLI and managed-block tests.
- [x] Implement repository-aware block version migration.
- [x] Update the `$aec` skill, CLI version, and documentation.
- [x] Pass skill, Debug, and Release validation.
- [x] Commit and push the exact v0.8 engine paths.
- [x] Create the initial `aec-data` commit, apply, and verify.

## v0.7 — Install the AEC skill during initialization

- [x] Confirm ordinary `aec init` is the initial skill-installation point.
- [x] Defer executable publishing and future skill upgrades.
- [x] Capture the user-authored `$aec` behavioral contract.
- [x] Scaffold and validate the minimal `aec` skill.
- [x] Add failing installation and containment tests.
- [x] Install skill metadata during ordinary `init`.
- [x] Keep `init --provider=chatgpt` repository-only.
- [x] Update version and user documentation.
- [x] Pass Debug and Release verification.
- [x] Prepare the exact engine release paths and staged secret gate.
- [x] Complete the source-first AEC validation and canonical commit/push.
