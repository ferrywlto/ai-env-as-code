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
- [ ] After review, choose the next bounded slice against the current code.

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
