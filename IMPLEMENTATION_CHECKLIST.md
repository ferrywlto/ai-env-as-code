# Implementation Checklist

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
- [x] Commit and push the exact macOS ARM64 publication paths.
- [x] Dogfood the installed Native AOT binary against the selected AEC repository.
- [x] Migrate the sole pre-release v0.8 skill installation to the v0.9 baseline.
- [x] Document the approved pulled-repository initialization, `apply` routing, and path-confirmation flow.
- [ ] Add and dogfood `aec upgrade` for future official skill versions.
- [ ] Complete v1.0 release validation, versioning, tagging, and publication.

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

## Immediate next work

Validate and release the completed v0.10 increment, then plan `aec upgrade`.
