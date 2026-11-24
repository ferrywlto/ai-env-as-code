# Implementation Checklist

## Production refactoring (#21)

- [x] Ferry reviewed the first slice: retire the test-only legacy Copilot backup path.
  The public portable command remains in use. Seven old direct-command cases
  map to live success, unchanged and binding tests; missing and unmanaged
  runtime refusals and runtime timestamp preservation are now covered there.
  Committed enrollment replaces the old untracked-source behavior, and
  unrelated untracked files are preserved by portable backup. The targeted
  Copilot and committer selection passed 19 tests; the combined unit and
  initialization selection passed 436 tests. Production code decreased by
  211 lines and test code by 204 lines.
- [x] Ferry reviewed the second slice: retire the test-only source-only initialization
  commit path and its exclusive helpers. The live environment commit test
  covers stale canonical instruction and configuration content; initialization
  tests retain legacy-baseline resume and failed-signing recovery coverage.
  The combined unit, initialization and backup selection passed 442 tests.
- [x] Ferry reviewed the third slice: remove the uncalled ordinary Codex backup wrapper
  and its unreachable personality branch. Initialization retains the preflight
  comparison; ordinary backup uses the portable command and its warning test.
  The combined unit, initialization and Codex backup selection passed 474 tests.
- [x] Ferry reviewed the fourth slice: remove unused application helpers for byte
  comparison, legacy argument parsing and managed-root validation, including
  the parser-only options record. Live parsing and path checks remain in use.
  The combined unit and focused CLI selection passed 369 tests.
- [x] Ferry reviewed the fifth slice: share portable capture post-commit parent, path,
  byte and mode verification with the existing migration commit proof. Retain
  capture-specific diagnostics, staged verification and write-time guards.
  The combined unit and four migration committer selections passed 426 tests
  in 10m 11s, including the legacy portable capture cases.
- [x] Ferry reviewed the sixth slice: share the Codex and Copilot pre-write branch and
  staged-index guard. Keep the separate check before staging and provider-specific
  errors. Added Codex staged-work coverage; the unit and portable backup selection
  passed 372 tests.
- [x] Ferry reviewed the seventh slice: replace four identical migration index-reader
  wrappers with one repository-based call to the existing path reader. Preserve
  index path, link checks, diagnostics and the timing of each read. The unit
  and focused migration selection passed 353 tests.
- [x] Ferry reviewed the eighth slice: share managed-block byte removal for Codex and
  Copilot while preserving provider parsing, byte-order marks and separators.
  The unit and block/uninstall selection passed 358 tests, including in-place
  Copilot digest coverage.
- [x] Ferry reviewed the ninth slice: move committed-file provenance readers from the
  Codex apply command to a repository-focused class. The reader and its Git
  adapters are unchanged; callers in both providers and migration use it. The
  unit, provenance, initialization, apply and focused review selection passed
  521 tests in 2m 20s.
- [x] Ferry approved the tenth slice: move shared repository-root and committed-HEAD
  checks from Codex apply into GitRepository. Their implementations, including
  detached-HEAD behavior and diagnostics, are unchanged. The focused run
  passed 367 tests with local socket access; the earlier sandboxed run aborted
  before execution when its local socket bind was denied.
- [x] Ferry reviewed the eleventh slice: move the Codex source/config committed-file
  delegates out of the apply writer; callers retain their pinned commits, byte
  reads and diagnostics. The unit, apply, initialization, portable backup and
  migration selection passed 553 tests in 4m 55s.
- [x] Ferry reviewed the twelfth slice: remove the test-only apply entry point and run
  its writer cases through attachment checkpoints. Preserve foreign-binding
  precedence and real Git provenance coverage. Unit, apply, initialization,
  provenance and portable review selection passed 494 tests in 1m 32s.
- [x] Ferry reviewed the thirteenth slice: combine the initialization and attachment
  checkpoint representation and shared comparisons while preserving their
  diagnostics, no-op order, provenance rereads and distinct write guards.
  Unit, apply, initialization, provenance and portable checks passed 494 tests
  in 5m 25s.
- [x] Ferry reviewed the fourteenth slice: share the reviewed backup snapshot input
  comparison while retaining fresh preview creation, provider-specific errors
  and Codex runtime configuration comparison. Unit and focused portable
  backup checks passed 382 tests.
- [x] Ferry reviewed the fifteenth slice: move the shared runtime path containment
  checks from Codex apply into RuntimePathGuard. The implementation and each
  caller's link-validation order and diagnostic labels are unchanged. The unit
  and focused path-safety selection passed 383 tests.
- [x] Ferry reviewed the sixteenth slice: share protected-target snapshot enumeration
  between migration source commits and target writes. Preserve required versus
  optional missing targets, path diagnostics and existing snapshot timing.
  Unit and focused migration checks passed 358 tests in 1m 30s.
- [x] Ferry reviewed the seventeenth slice: share exact staged-path verification for
  portable capture and the other migration committers. Keep per-file proofs,
  diagnostic text and verification order unchanged. Unit and focused migration
  checks passed 360 tests in 2m 50s.
- [x] Ferry reviewed the eighteenth slice: reuse the pinned tree entry when validating
  migration scaffold source blobs. Preserve missing-file handling, regular-file
  validation, bounded reads, LFS rejection and no-replacement-object checks.
  Unit and focused migration preview checks passed 364 tests in 39s.
- [x] Ferry reviewed the nineteenth slice: remove the unused non-empty-only option
  from the initialization commit and keep its fixed commit message and required
  empty second commit behavior. Unit, initialization and backup checks passed
  461 tests in 52s.
- [x] Ferry reviewed the twentieth slice: share historical-target pinning logic between
  status and completion, retaining independent reads and final drift checks.
  Unit and status/completion preview checks passed 362 tests in 2m 14s.
- [x] Ferry reviewed the twenty-first slice: share the protected-file inventory between
  scaffold writing and source validation while retaining their separate file
  readers, diagnostic labels and validation checkpoints. Unit and focused
  migration checks passed 373 tests in 3m 16s.
- [x] Ferry reviewed the twenty-second slice: reuse immutable historical source blobs
  within a single portable migration preview. Per-provider working-file and
  final drift checks remain. A matched dual-provider test passed before and
  after, with Git starts falling from 207 to 201; both ran in about four seconds.
  Unit and focused migration checks passed 358 tests in 2m 10s.
- [x] Cumulative Debug regression passed for `35683d9` in three disjoint
  runs. The 754 routine tests passed in 3m 29s, the isolated public recovery
  case in 1m 18s, and the remaining 627 legacy migration tests in 48m 45s.
  The three TRX files matched all 1,382 cases in unfiltered discovery by test
  name and multiplicity, with no failures, skips or unmatched cases. Results
  are in `artifacts/refactor-verify.uGZNHJ/` and
  `artifacts/refactor-legacy.JnrGCW/`. The earlier unfiltered attempt at
  `ec4ad1e` stopped during the public recovery case after 457 passes when
  the two-minute watchdog fired.
- [x] Ferry reviewed the fixture fix. Managed fixtures now default to the test build
  output. The Git environment and committed-file provenance fixtures use the
  same default. `AEC_TEST_TEMP_ROOT` remains available as an override. With the
  override unset, all 52 focused tests passed and Git Trace2 recorded every
  observed fixture worktree under the test build output.
- [x] Ferry reviewed the fixture naming correction. The routine run at `d18a5d9`
  passed 753 of 754 tests. The conditional Git identity test failed because
  `fixtures` and the existing `Fixtures` directory aliased on the local
  case-insensitive filesystem. With the fixture default renamed to
  `test-work`, all 754 routine tests passed in 3m 29s. The isolated recovery
  case passed in 1m 18s with 3,833 Git starts and a five-minute watchdog.
  Both runs used the default fixture root. See
  `artifacts/refactor-verify.uGZNHJ/routine.trx` and `recovery.trx`
  in the same directory.
- [x] Native macOS ARM64 AOT build and isolated installer, provider lifecycle,
  render and migration smoke passed at `ec4ad1e`; smoke fixtures used a
  repository-local temporary directory. The production source at `35683d9`
  matches `ec4ad1e` exactly. Real-harness checks are tracked in #4.
- Compared with `418650a`, production C# is down 646 lines and test C# is down
  154 lines. The reviewed slices above record the removed paths and retained
  coverage.
- [x] Ferry reviewed the shared portable instruction target digest. Codex, Copilot and
  backup attribution use the same SHA-256 computation and lowercase format
  rule. The unit and focused portable selection passed all 353 tests.
  At PR candidate `bc71333`, all 754 routine Debug tests passed in 3m 52s
  and the macOS ARM64 native installer, lifecycle, render and migration
  smoke passed. The complete 1,382-case regression above covers the
  production revision before the digest extraction.
- [ ] Complete Ferry's final review and ticket acceptance.

## Routine regression refactor

[Issue #19](https://github.com/ferrywlto/ai-env-as-code/issues/19) is in review in
[PR #20](https://github.com/ferrywlto/ai-env-as-code/pull/20).
The [routine regression](docs/development.md#routine-regression) stays sequential.
It covers current workflows and Git-free migration decisions. The separate
`Category=LegacyMigration` selection covers the real-Git 1.x to 2.0 workflow.

| Verification milestone | Passed | Wall time | Git starts |
| --- | ---: | ---: | ---: |
| First completed routine baseline | 840 | 11m 19s | 38,385 |
| First single routine run under five minutes | 761 | 4m 54s | 12,118 |
| First routine run under 10,000 Git starts | 762 | 3m 44s | 9,904 |
| Latest routine run after initialization fixture consolidation | 758 | 3m 21s | 9,364 |
| Latest dedicated unit run | 340 | 1.28s | 0 |

Routine runs used prebuilt Debug binaries and matched TRX results against
discovery. The case counts reflect consolidation of overlapping tests.
The latest routine results and Git trace are in
`artifacts/five-minute-init-fixtures-routine.iIu1Wc`.

The earlier unfiltered run stopped at 34m 52s after 52,660 Git starts, with
no final suite result. Trace2 showed Git was still completing commands
when the recovery test hit the watchdog. The focused public recovery
case later passed in 68.97s with 4,661 Git starts under the two-minute
per-test guard. It still verifies refusal before prompting, a real retry,
a marker-only commit with the expected parent, unchanged runtime bytes,
and a clean checkout. Separate tests retain two-provider completion.

- [x] Record a completed routine baseline and verify the routine suite
  under five minutes.
- [x] Preserve the distinct Git and filesystem contracts while consolidating
  fixtures and moving content-only assertions into memory.
- [ ] Record the two consecutive unfiltered regression passes required by
  #19, including legacy migration.
- [ ] Complete Ferry's review of the #19 coverage and verification evidence.

## Focused real-Git regression checks

The fast feedback milestone [#9](https://github.com/ferrywlto/ai-env-as-code/issues/9)
delivered the [Git-free unit command](https://github.com/ferrywlto/ai-env-as-code/pull/16),
[in-memory decisions](https://github.com/ferrywlto/ai-env-as-code/pull/17), and
[focused real-Git review](https://github.com/ferrywlto/ai-env-as-code/pull/18).
The focused review passed 12 portable cases and five migration cases at
acceptance. The commands are in the [development guide](docs/development.md#focused-real-git-review).

| Area | In-memory coverage | Retained Git and filesystem coverage |
| --- | --- | --- |
| Apply and status | [Byte comparisons and config decisions](tests/Aec.Tests/PortableApplyStatusDecisionTests.cs), [TOML parsing](tests/Aec.Tests/CodexRuntimeConfigParsingTests.cs) | [Committed target provenance](tests/Aec.Tests/CommittedTargetValidatorTests.cs), [stale inputs and partial writes](tests/Aec.Tests/SharedTargetStatusTests.cs), and provider CLI output and exit codes |
| Backup | [Authored-source attribution](tests/Aec.Tests/PortableBackupAttributionTests.cs) | [Exact commit paths, bytes, and modes](tests/Aec.Tests/PortableBackupCommitterTests.cs), filter and signing refusals, and unchanged unrelated state |
| Initialization and transport | [Arguments and provider-home selection](tests/Aec.Tests/InitArgumentTests.cs), instructions and config transformations | [Fresh initialization, checkout binding, and failed-commit recovery](tests/Aec.Tests/InitTests.cs). Both providers retain [push, clone, fetch, fast-forward, and apply](tests/Aec.Tests/GitRoundTripTests.cs). |
| Migration | [Apply review](tests/Aec.Tests/MigrationApplyReviewTests.cs), [target review](tests/Aec.Tests/MigrationTargetReviewTests.cs), and capture and scaffold decisions | [Exact commits and signing failures](tests/Aec.Tests/MigrationPortableCaptureCommitterTests.cs), [stale state and partial writes](tests/Aec.Tests/MigrationApplyCoordinatorTests.cs), [marker recovery](tests/Aec.Tests/RegressionFollowupTests.cs), and the [legacy workflow](tests/Aec.Tests/LegacyMigrationTests.cs) |

## AEC 2.0 milestones

| Milestone | Reviewed evidence |
| --- | --- |
| [2.0 alpha acceptance](https://github.com/ferrywlto/ai-env-as-code/issues/2) | **25/25 (100%)** under baseline v3. Actual Codex-copy rehearsal and a synthetic Copilot rehearsal passed. Native migration smoke passed on macOS ARM64, Linux ARM64, and Windows x64. Historical H4 verification passed 1,441 Debug cases and accounted for all 1,441 Release cases across batches. |
| [Migration filter handling](https://github.com/ferrywlto/ai-env-as-code/issues/5) | 84 regression cases passed. Real Git verified filter suppression, refusal of selected filters and managed LFS pointers, and exact fast-forward preservation. macOS ARM64 Native AOT lifecycle and migration smoke passed. |
| [2.0.0 version metadata](https://github.com/ferrywlto/ai-env-as-code/pull/7) | Merged. The development executable reports `2.0.0`. The [release notes](docs/release-notes-2.0-draft.md) remain a draft. |
| Documentation and landing page | [Documentation cleanup](https://github.com/ferrywlto/ai-env-as-code/pull/12) preserved all 18 diagrams. Ferry reviewed the rendered [2.0 landing page](https://github.com/ferrywlto/ai-env-as-code/pull/10) and its links. |

- [ ] Real Codex and GitHub Copilot harness verification remains deferred to
  [#4](https://github.com/ferrywlto/ai-env-as-code/issues/4). Native smoke tests
  and the synthetic Copilot rehearsal are separate evidence.
- [ ] Ferry verifies migration independently before requesting an official
  2.0 release.
- The provider-first CLI and installer changes are deferred to
  [v3 #3](https://github.com/ferrywlto/ai-env-as-code/issues/3).
- The migration CLI and [migration guide](docs/migration.md#migration-operating-precondition)
  tell operators to close affected harness sessions and pause other writers.
  Broader migration validation consolidation remains a follow-up.

## Record ownership

Track scope, acceptance checks, and review in the ticket. Count verified and
reviewed outcomes against the ticket's published Definition of Done. Update this
checklist in place with reviewed outcomes, outstanding gates, and evidence links.
Use Git history and test artifacts for individual measurements and failed attempts.

- [Checklist before compaction](https://github.com/ferrywlto/ai-env-as-code/blob/7444acdbd037e3202c368243b2cb3a76f23d46ae/IMPLEMENTATION_CHECKLIST.md)
