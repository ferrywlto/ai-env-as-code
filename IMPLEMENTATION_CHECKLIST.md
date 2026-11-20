# Implementation Checklist

## Whole-suite regression baseline

[Ticket #19](https://github.com/ferrywlto/ai-env-as-code/issues/19) was expanded
and reactivated on 19 November 2026. The earlier portable cleanup remains
accepted. The expanded work targets the cost and reliability of the complete
test suite.

- Shared the existing `AEC_TEST_TEMP_ROOT` override across 22 fixture files.
  The default roots are preserved. Baseline runs use disposable provider homes
  and Git configuration inside the approved Codex workspace.
- The isolated migration recovery case was stopped by VSTest after 301.06 s.
  Git Trace2 recorded 12,715 process starts, including 6,271 filter-config
  probes. Git was still completing commands 299.96 s after the first start.
  The largest gap between completed commands was 60 ms. This run shows the
  watchdog interrupted continuing Git work.
- The remaining Debug run excluded the isolated recovery case. It recorded
  980 passes and zero reported failures over about two hours of console
  output. The execution session was lost and the process was gone when
  checked. The run produced no final summary or TRX, so this is a partial
  baseline.
- Of the completed cases, `RegressionFollowupTests` accounted for about
  26.6 minutes. The portable migration capture committer, preview, and
  superseded writer suites accounted for another 33.3 minutes. These totals
  come from the console's rounded per-case durations.

### Migration recovery and capture cleanup

- The recovery test now starts from committed, applied targets and an
  uncommitted marker. It still verifies the CLI refuses the marker before
  prompting and preserves the checkout. After the marker is cleared, one
  public retry must commit only the marker, keep the previous HEAD as its
  parent, leave runtime bytes intact, and finish with a clean checkout.
  `MigrationCompletionWriterTests.IgnoredMarkerStopsWithCreatedFileRetained`
  exercises the real ignored-marker write failure used to reach that state.
- With the same Trace2 settings and five-minute watchdog, the revised
  recovery test passed in 183.88 s. Git starts fell from 12,715 at the
  previous abort to 6,597 at successful completion. Filter probes fell from
  6,271 to 3,211. The old figures are lower bounds for the original test.
- Retired 20 real-Git fixture cases across the superseded capture writer and
  capture preview suites. Those cases accounted for about 527 s of rounded
  test time on the interrupted baseline. Eight distinct old-writer cases
  moved to the live committer without increasing the total retained count.

| Retired or transferred capture cases | Retained coverage |
| --- | --- |
| Superseded writer no-op and mixed-format tests | Live committer verifies exact unchanged fixture bytes and preservation of mixed-format runtime and drafts |
| Superseded writer rejected combined provider edits | Live committer verifies combined same-source proposals and an exact single commit for distinct overlays and managed config |
| Superseded writer single-provider paths, config, and stale review cases | Live committer covers both providers' local and shared source paths, one provider plus config, peer drift, missing reviewed provider, and altered reviewed target bytes |
| Superseded writer staged drafts, signing, and altered proposals | Live committer checks exact staging preservation and its diagnostic, signing failure and unchanged HEAD, and fresh proposal derivation |
| Capture preview path and managed config variants | Attribution unit suite verifies exact proposals. Live committer verifies exact committed paths and config. The preview retains conflict and whole-fixture no-write checks. |

The focused run passed all 220 cases in 620.54 s. It included 187 unit tests,
the entire live capture committer suite, and the retained conflict and
ignored-marker checks. The recovery case passed separately. Ferry accepted
the migration cleanup on 20 November 2026 (`9cc2a0e`).

### Draft recovery cleanup

- Replaced two public tests that each invoked migration twice with one
  public retry from four seeded scaffold files and one focused post-DONE
  refusal for an untracked reserved-marker draft. The retry preserves the
  shared draft and commits only the completion marker in its final commit.
  The focused refusal checks the exact source-phase failure and preserves
  every byte of the fixture. The existing EOF/cancellation and scaffold
  preservation cases retain their separate real-Git assertions.
- The two retired cases took about 417 s in the earlier console baseline.
  The replacement run passed in 143.83 s wall time with Trace2, including
  137.94 s for the public retry and 4.65 s for the focused refusal. It
  recorded 7,117 Git starts, including 3,342 filter probes. The earlier
  baseline recorded rounded test durations and did not trace Git.
- All 187 unit tests and six retained EOF, cancellation, and scaffold
  preservation checks passed in 66.40 s. Ferry accepted the draft recovery
  slice on 20 November 2026 and approved these focused results for pushing
  this checkpoint (`71ddd66`).

### Target-writer fixture preparation

- The target-writer tests now prepare a single capture plan, prepare the
  target writer from it, and write sources through that same plan. This
  matches the live portable capture committer and removes one full Git
  preparation from each of 28 cases. The existing assertions and timing
  of injected stale inputs and write failures are preserved.
- The same five cases passed before and after on macOS ARM64. Both runs
  used a prior Debug build, workspace fixtures, TRX, and Git Trace2.

| Measurement | Before | After |
| --- | ---: | ---: |
| Passing cases | 5 | 5 |
| Git process starts | 4,452 | 3,336 |
| Wall time | 111.69 s | 74.24 s |

The remaining 23 affected cases passed alongside all 187 unit cases in
343.05 s. All 28 affected target-writer cases passed across both runs. Ferry
accepted the fixture preparation change on 20 November 2026 (`d76ff21`).

### Source-writer overlap cleanup

Retired 13 source-wrapper cases and transferred two distinct stale-input
cases to the live committer. The remaining seven source-only cases still
check exact authored writes, preserved config and unmanaged bytes, conflict
after review, and partial source-replacement reporting.

| Removed source-wrapper cases | Retained real-Git coverage |
| --- | --- |
| Two no-op and two mixed-format cases | `ValidatedNoopPreservesEveryFixtureAndGitByte` and `MixedFormatsPreserveLegacyRuntimeAndUnconsumedAuthoredDraft` check exact fixture or committed paths and unchanged legacy contents |
| Staged draft and caller-supplied proposal | `ExistingStagedAndUnstagedDraftStopBeforeCapture` preserves the fixture and exact refusal. `CallerDerivedProposalsCannotRedirectCombinedCapture` checks the recomputed source bytes and exact commit. |
| Seven stale-input rows | `StaleInputsStopBeforeFilesStagingOrCommit` retains HEAD, branch, changed peer runtime, config, and marker cases. It now also checks a Codex runtime edit and an authored draft appearing after review. |

Both measurements used the same case filter, Debug build, workspace fixture
settings, and Git Trace2 on macOS ARM64.

| Measurement | Before | After |
| --- | ---: | ---: |
| Passing cases and disposable fixture setups | 29 | 18 |
| Git process starts | 17,493 | 11,045 |
| Git init commands | 87 | 54 |
| Wall time | 459.67 s | 321.68 s |

All seven retained source-only cases and 187 unit cases passed separately
in 125.28 s. Ferry accepted the source-writer cleanup on 20 November 2026.

## Portable integration test cleanup

[Ticket #19](https://github.com/ferrywlto/ai-env-as-code/issues/19) was accepted
by Ferry on 19 November 2026.
The first slice removes five redundant Copilot backup preview cases and keeps
the real-Git refusal for a Copilot v1 runtime without a target digest.

| Removed preview cases | Retained coverage |
| --- | --- |
| Unchanged runtime | `PortableBackupAttributionTests.UnchangedRuntimeProducesNoProposal` and `PortableCopilotBackupWriterTests.NoChangeDoesNotCreateAnEmptyCommit`, which now also checks the reviewed provider, HEAD, no proposal, and unchanged runtime bytes |
| Shared, platform, and provider edits | `PortableBackupAttributionTests.OneEditedSectionProposesItsAuthoredSource` and `SharedTargetStatusTests.BackupAttributionPreviewIdentifiesOneAuthoredSection` retain the three paths, proposed bytes, and proof the preview leaves the checkout and runtime unchanged |
| Multiple section edits | `PortableBackupAttributionTests.EditsToMultipleSectionsAreRejected` and `PortableCopilotBackupCommandTests.MultipleEditedSectionsStopWithoutCommit`, which now also checks unchanged runtime bytes |

Measured the same `FullyQualifiedName~PortableCopilotBackup` filter before
and after on macOS ARM64. Both runs used a prior Debug build, `--no-build
--no-restore`, isolated workspace fixtures, TRX, and Git Trace2.

| Measurement | Before (`3941f5b`) | After |
| --- | ---: | ---: |
| Passing real-Git cases | 20 | 15 |
| Git process starts | 1,231 | 1,077 |
| Git init / add / commit commands | 20 / 26 / 23 | 15 / 21 / 18 |
| Wall time | 111.36 s | 132.74 s |

The five retired cases took 13.45 seconds in the baseline. All 14 retained
cases with matching names ran slower in the second measurement, so this pair
shows less Git work and leaves wall-clock savings unverified. Separately, all
180 unit tests and all six retained shared attribution cases passed together.
Ferry accepted the Copilot cleanup and timing evidence on 19 November 2026.

The next slice removes three repeated local Git config commands from each of
the Codex and Copilot backup command and writer fixtures. The shared
`GitTestEnvironment` already supplies an isolated author and signing default.
The same four real-Git commit cases passed before and after. Trace2 counted
835 process starts before and 823 after, including 12 redundant config calls
removed. Wall time was 19.28 seconds before and 21.28 seconds after. A
separate verification passed all 37 affected backup cases and all 180 unit
tests in 52.70 seconds. Ferry accepted the Git setup slice on 19 November 2026.

### Apply and status file I/O cleanup

| Original cases | Change | Retained coverage |
| --- | --- | --- |
| `SharedTargetStatusTests.ApplyPreviewReportsRuntimeEditsWithoutWriting` | Retired one overlapping case | `PortableApplyStatusDecisionTests.ApplyAndStatusCompareExactInstructionBytes` checks the replacement decision and exact desired bytes. `ApplyPreviewWouldUpgradeOnlyTheLocalBlock` checks a changed preview leaves runtime and checkout intact. |
| `PortableEnvironmentStatusTests.InstructionDriftIsDifferent` | Retired two overlapping cases | The apply/status unit suite checks drift for both providers. `StatusTests.ReportsDifferentForDifferentFiles`, `StatusDoesNotChangeSourceOrTargetContent`, and `PortableCopilotStatusCommandTests.ReportsRuntimeDriftWithoutWrites` cover both CLI paths, exit codes, and file preservation. |
| `PortableEnvironmentStatusTests.UnmanagedCodexConfigValuesDoNotCountAsDrift` | Retired one overlapping case | `PortableEnvironmentStatusTests.MatchingManagedRuntimeIsInSyncWithoutWrites` exercises unmanaged model settings, exact config bytes, and unchanged HEAD. |
| Seven `StatusTests` cases from `AcceptsAQuotedManagedKey` through `IgnoresAnUnrelatedLiteralKeyContainingABackslash` | Moved seven cases to `CodexRuntimeConfigParsingTests` with direct TOML bytes | Includes both key escape forms, multiline string and nested array handling, decoded values, ambiguous multiline values, and literal keys. `StatusTests.ReportsInSyncForEqualFiles` and `FailsWhenRuntimeConfigDeclaresPersonalityTwice` retain the representative CLI success and refusal. |

Measured the same case filter before and after on macOS ARM64 with a prior
Debug build, `--no-build --no-restore`, TRX, Git Trace2, and identical workspace
fixture settings. The status fixture uses the existing `AEC_TEST_TEMP_ROOT`
override in both measurements.

| Measurement | Before | After |
| --- | ---: | ---: |
| Real-Git cases | 24 | 13 |
| In-memory cases in the selected group | 0 | 7 |
| Git process starts | 1,265 | 916 |
| Git init / add / commit commands | 24 / 24 / 24 | 13 / 13 / 13 |
| Wall time | 24.63 s | 22.18 s |

The fast suite passed all 187 tests with zero Git calls. All 12
`PortableReview` real-Git checks passed. Continue with the whole-suite baseline.

## Focused real-Git regression checks

[Ticket #15](https://github.com/ferrywlto/ai-env-as-code/issues/15) was accepted under
[test suite refactoring #9](https://github.com/ferrywlto/ai-env-as-code/issues/9).
The [migration review command](docs/development.md#focused-real-git-review)
passed all five cases in 78.56 seconds on macOS ARM64, including build and test
startup. Repository fixtures and isolated Git config stayed inside the checkout.

| Migration review check | Contract exercised | TRX duration |
| --- | --- | ---: |
| `SeparateOverlaysAndManagedConfigShareOneCommit` | Exact parent, paths, committed bytes and modes, and clean index | 19.07 s |
| `SelectedFilterFailsWithoutRunningItsCommand` | External Git filter suppression | 3.47 s |
| `SigningFailureRetainsExactCombinedStagingWithoutRollback` | Signing failure preserves HEAD and staged proposal bytes | 17.78 s |
| `PostWriteDriftRefusesBeforeStagingAndRetainsState` | Concurrent runtime drift stops completion and preserves the marker, index, and HEAD | 16.33 s |
| `MissingSourcesBecomeEmptyFilesAndFreshRetryIsANoop` | Scaffold retry preserves existing artifacts | 18.38 s |

Public-command recovery after a partial migration remains covered by
`RegressionFollowupTests` in the full suite. Ferry accepted the migration slice
on 19 November 2026 (`896ba77`).

The [portable review command](docs/development.md#focused-real-git-review)
passed all 12 cases in 34.81 seconds on macOS ARM64. Both focused groups honor
the workspace fixture location. The fast suite also passed all 180 tests in
1.21 seconds with build and restore already up to date and zero Git calls.

| Portable review cases | Contract exercised | TRX duration |
| --- | --- | ---: |
| Codex backup combined drift and Copilot backup single-section drift | Exact commit parent and paths with runtime preserved | 5.54 s and 5.24 s |
| Codex stale target and Copilot unrelated staged work | Refuse backup before changing sources or config | 0.78 s and 1.35 s |
| Codex apply (three drift combinations) and Copilot apply | Write only managed files and preserve unrelated config and repository state | 3.16–4.17 s each for Codex, 2.59 s for Copilot |
| Codex apply after HEAD moves | Report partial update and preserve instruction bytes | 1.98 s |
| Codex invalid config, Codex legacy instruction drift, and Copilot instruction drift | Validate config with missing instructions, report drift, and preserve files | 0.70 s, 0.71 s, and 0.67 s |

Ferry accepted the portable selection and coverage map on 19 November 2026.
Next, publish the reviewed branch.

## Completed increment — in-memory regression coverage

[Ticket #14](https://github.com/ferrywlto/ai-env-as-code/issues/14) was accepted under
[test suite refactoring #9](https://github.com/ferrywlto/ai-env-as-code/issues/9).
The backup attribution slice adds 14 in-memory cases across Codex and Copilot
for unchanged runtime, single-section edits, multiple-section edits, stale
targets, and missing target digests. The fast suite passed 141 tests in
3.41 seconds wall time on macOS ARM64 with zero Git calls.

Coverage map: [PortableBackupAttributionTests](tests/Aec.Tests/PortableBackupAttributionTests.cs)
covers the decision and exact authored path. The
[Codex](tests/Aec.Tests/PortableCodexBackupCommandTests.cs) and
[Copilot](tests/Aec.Tests/PortableCopilotBackupCommandTests.cs) CLI suites retain
real-Git coverage for reading the committed target, writing the source, rendering
affected targets, and exact commits and refusals. Backup preview and CLI tests
remain in place. Ferry accepted the backup slice on 18 November 2026
(`7c24e34`).

The apply/status slice adds 17 in-memory cases for exact, different, and
missing instruction bytes, Codex personality updates, and the combined status.
[PortableApplyStatusDecisionTests](tests/Aec.Tests/PortableApplyStatusDecisionTests.cs)
covers those decisions. The four portable apply and status CLI suites verify
real reads and writes, status output and exit codes, and Codex validation when
instructions are missing. All 40 focused CLI cases passed in 43.07 seconds.
The fast suite passed all 158 tests in 4.31 seconds wall time on macOS ARM64
with zero Git calls. Ferry accepted the apply/status slice on 18 November 2026
(`137875f`).

The migration slice adds 22 in-memory cases in
[MigrationApplyReviewTests](tests/Aec.Tests/MigrationApplyReviewTests.cs).
It covers matching reviews, changes to branch, commit, harness and sources,
instruction and config bytes, and planned write flags.
[MigrationApplyCoordinatorTests](tests/Aec.Tests/MigrationApplyCoordinatorTests.cs)
and the [Codex](tests/Aec.Tests/MigrationCodexApplyWriterTests.cs) and
[Copilot](tests/Aec.Tests/MigrationCopilotApplyWriterTests.cs) writers retain
real-Git coverage for provider order, stale inputs before the first write,
tampered buffers, and partial failures. Five focused coordinator and writer
cases passed in 87.91 seconds with disposable fixtures inside the workspace.
The fast suite passed all 180 tests in 4.71 seconds wall time on macOS ARM64
with zero Git calls. Ferry accepted the migration slice and coverage map on
18 November 2026. [PR #17](https://github.com/ferrywlto/ai-env-as-code/pull/17)
merged to `main` and #14 closed after the approved fast and focused checks.

## Completed increment — fast unit test command

[Ticket #13](https://github.com/ferrywlto/ai-env-as-code/issues/13) was accepted
by Ferry on 18 November 2026 under
[test suite refactoring #9](https://github.com/ferrywlto/ai-env-as-code/issues/9).
Seven in-memory test classes are tagged `Category=Unit`. The documented command
passed all 127 tests on macOS ARM64 with .NET SDK 10.0.404 in 5.33 seconds wall
time, including restore, build, and test startup. A separate run with a
positive-controlled Git shim passed all 127 tests and recorded zero Git calls.
The initial sandboxed timing and shim attempts stopped at NuGet audit access
and the test runner's local socket, respectively. Both checks passed with the
approved permissions. [PR #16](https://github.com/ferrywlto/ai-env-as-code/pull/16)
merged to `main` on 18 November 2026 and #13 closed. Ferry approved using the
fast-suite result for this PR after the full Debug run timed out.

## Completed increment — documentation clarity

[Ticket #11](https://github.com/ferrywlto/ai-env-as-code/issues/11) is Done.
Ferry approved the final documentation on 17 November 2026. Simplified the README,
reference, migration and development guides; removed repeated migration internals
and collapsed technical examples and evidence. All 18 original diagrams are
unchanged. Local links/anchors, expandable sections and `git diff --check` passed.
Ferry approved committing the changes on `11_improve_documentation`; publication is separate.

## Completed increment — 2.0 landing page

[Ticket #8](https://github.com/ferrywlto/ai-env-as-code/issues/8) is Done. Ferry approved the landing-page change and verified that the rendered layout and links work as expected on 17 November 2026. Content was reviewed against the 2.0 documentation and `git diff --check` passed. [PR #10](https://github.com/ferrywlto/ai-env-as-code/pull/10) contains the approved change.

## Completed increment — 2.0.0 version metadata

[Ticket #6](https://github.com/ferrywlto/ai-env-as-code/issues/6) is Done; [PR #7](https://github.com/ferrywlto/ai-env-as-code/pull/7) merged to `main` on 16 November 2026. The focused version test passed and the executable reports `2.0.0`. An official release and installation remain separate.

## Current increment — migration filter handling

[Ticket #5](https://github.com/ferrywlto/ai-env-as-code/issues/5) is Done. Migration suppresses configured external filters for Git commands, rejects selected filters and managed LFS pointers, and refuses submodules. After correcting the fsmonitor guard ordering, all 84 command, preparation, fast-forward and source-validation regression cases passed (14m 35s), using fixtures under the allowed `.codex` workspace. Ferry approved the reviewed change on 13 November 2026. Do not run live migration from this harness.

On 15 November, three additional cases passed: complete migration with isolated global `filter.lfs` commands, rejection of a filter on a generated instruction path, and byte preservation of an unrelated tracked configuration file across fast-forward. A further fast-forward case verified exact incoming notes bytes and unchanged configuration bytes.

A later line-ending attribute experiment failed at its setup: Git reported `notes.md` as modified rather than clean after the fixture attempted to convert it to CRLF. It did not reach fast-forward; the unfinished test was removed without retry. Ferry chose normal Git conversions for upstream-updated files on 15 November. Two additional cases passed for local edits hidden by `assume-unchanged` and `skip-worktree`. Ferry approved the final changes; implementation was committed as `247bbfb`. An official release remains separate.

The macOS ARM64 Native AOT build on 13 November was stopped after it remained in `dotnet publish` for over three minutes. With Ferry's approval, the 15 November retry completed; the arm64 artifact reports `2.0.0-alpha.4`. The disposable macOS installer, lifecycle, render and migration smoke passed with global `filter.lfs` entries present and unchanged. The personal executable was not installed or modified.

## Current increment — AEC 2.0 alpha

[Ticket #2](https://github.com/ferrywlto/ai-env-as-code/issues/2) covers the completed 2.0 alpha work.
Ferry will verify migration independently before requesting an official release.
Current reviewed engine checkpoint: macOS ARM64 Native AOT migration smoke (`8599e59`).
Ticket #2 alpha completion is recorded, DoD 25/25 (100%), baseline v3.

| Current item | State / immediate next action |
| --- | --- |
| 13e.7: read-only migration apply preview | Reviewed and committed: `f4717c6` + `4f1639e`. Release warning-as-error build: zero warnings/errors; 117/117 focused checks passed with strict index/whole-fixture snapshots. No push or new release claim. |
| 13e.8: Copilot migration apply | Reviewed and committed: `feb4641`. Release warning-as-error build: zero warnings/errors; 55/55 focused checks passed. No push or personal deployment. |
| 13e.9: Codex migration apply | Reviewed and committed: `42c98be`. Release warning-as-error build: zero warnings/errors. Focused tests: 51/51 passed (17 new Codex migration, 22 existing Codex writer/apply, 12 Copilot migration). Architecture/reference and migration operating precondition committed separately in `a51eacf`; warning output is not implemented. No push or personal deployment. |
| 13e.10: enrolled-provider apply coordination | Reviewed and committed: `4ca3ce2`. Release warning-as-error build: zero warnings/errors; 44/44 focused tests passed (15 coordinator, 29 existing provider writers). No completion marker, status verification, public CLI, push or personal deployment. |
| 13e.11: post-apply status gate | Reviewed and committed: `55237f7`. Release warning-as-error build: zero warnings/errors; 27/27 focused tests passed (13 gate, 14 portable-status regressions). No marker, repair, public CLI, push or personal deployment. |
| 13e.12: completion preview | Reviewed and committed: `dd85526`. Release warning-as-error build: zero warnings/errors (3.21s); 21/21 focused tests passed (8 preview, 13 status-gate regressions). No marker write, public CLI, push or personal deployment. |
| 13e.13: completion marker creation | Reviewed and committed: `bb4227c`. Release warning-as-error build: zero warnings/errors (25.58s); 104/104 focused tests passed (14 writer, 90 guard regressions). No marker staging/commit, public CLI, push or personal deployment. |
| 13e.14: completion marker commit | Reviewed and committed: `603922e`. Release warning-as-error build: zero warnings/errors (final 2.89s); 26/26 focused checks passed (12 committer, 14 writer). No public CLI, personal deployment or push. |
| 13e.15: post-DONE completion coordination | Reviewed and committed: `4f4c2df`. Release build: zero warnings/errors; 13/13 focused tests passed. |
| 13e.16: guarded capture | Reviewed. Capture routes by enrolled runtime format and refuses mixed formats before writing. It preserves the original history anchor and section choice. Release build: zero warnings/errors; 46/46 focused checks passed. |
| 13e.17: capture to scaffold handoff | Reviewed. The coordinator passes the captured checkout to the existing scaffold preview and writer. Release build passed with zero warnings or errors. All 14 focused tests passed. |
| 13e.18: editing to completion handoff | Reviewed. Exact `DONE` starts fresh validation and completion. EOF or cancellation leaves the scaffold available for editing. Release build passed with zero warnings or errors. All 16 focused tests passed. |
| Completed boundary | Source/target commits, apply preview, both provider writers, enrolled-provider apply coordination and post-apply status verification are reviewed. |
| Native migration smoke | macOS ARM64 passed locally at `8599e59`; Linux ARM64 and Windows x64 passed at `d6af1ad`. All three results reviewed. |
| Alpha completion | Retained `2.0.0-alpha.4` at the time of the approved alpha checkpoint. Official release was deferred pending Ferry's own migration verification and later request. |

Evidence: [initial diagnostics](artifacts/migration-apply-preview-20261101/mutation-diagnostic/),
[command traces](artifacts/migration-apply-preview-20261101/trace2-permitted/),
[failed refresh-toggle attempt](artifacts/migration-apply-preview-20261101/auto-refresh-fix/),
[verified content/mode fix](artifacts/migration-apply-preview-20261101/content-mode-fix/),
[Copilot migration apply results](artifacts/migration-copilot-apply-20261101/) (writer 7m34s; preview 13m53s),
[Codex migration apply results](artifacts/migration-codex-apply-20261102/) (new writer 3m31s),
[coordinator verification](artifacts/migration-apply-coordinator-20261102/verification.md) (coordinator 5m11s; writer regressions 5m30s).
Status gate: [27 passing checks](artifacts/migration-status-gate-20261102/verification.md) (gate 1m29s; regressions 11s).
Completion preview: [21 passing checks](artifacts/migration-completion-preview-20261102/verification.md) (preview 1m20s; status-gate regressions 1m9s).
Marker writer: [104 passing checks](artifacts/migration-completion-writer-20261102/verification.md) (writer about 15m; regressions 26m39s).
Marker committer: [26 passing checks](artifacts/migration-completion-committer-20261102/verification.md) (original committer 3m44s; writer regressions 4m34s; added boundary case 21s).
Post-DONE coordinator: [13 passing checks](artifacts/migration-post-done-20261102/verification.md), including first migration from legacy files.
Implementation chat: `019f7f16-046b-7693-a1d5-b32f284e15d5`.
See [reference](docs/reference.md), [architecture](docs/architecture.md) and
[shared-instruction design](docs/shared-instructions-design.md) only as needed.

## DoD completion metric

**100% — 25/25 alpha acceptance checks complete**, baseline v3 (12 November 2026).
D2, E4, H2 and H4 completed after review of their evidence. The authoritative checks
(A1–I2) are in [ticket #2](https://github.com/ferrywlto/ai-env-as-code/issues/2#definition-of-done).
This is accepted-outcome progress, not effort, ETA or release readiness.
Reviewed slices provide evidence; they are not the denominator.

After each slice, the implementation workflow must update affected ticket DoD
check statuses and concise evidence, recompute completed/total (nearest whole
percent), and replace this metric in place before reporting the slice complete.
Credit requires required verification plus Ferry review; work awaiting either
stays unchecked. Retain failed-attempt details in chat/artifacts, not this index.
Ferry approved deferring F1/F2 to #4 to protect credentials and personal runtime;
baseline v2 removed those two unverified checks. Baseline v3 changes H3, I1 and I2
to cover alpha completion; an official release is separate and requires Ferry's
own migration verification and later request.

## Remaining 2.0 acceptance gates

- [x] Review fresh-start failure and retry fixtures (C6).
- [x] Rehearse with approved actual runtime copies (D2). Actual Codex-copy rehearsal and separate synthetic Copilot migration passed and were reviewed; the synthetic fixture is not actual Copilot or real-harness evidence.
- [x] Review the passing Linux ARM64 and Windows x64 native migration coverage (E4).
- [x] Review CLI help, bundled skills, README and reference alignment (H1).
- [x] Review the passing Windows runner verification of the H2 generated-uninstaller guard fixes.
- [x] Retain alpha version metadata and the [release notes draft](docs/release-notes-2.0-draft.md) (H3). Release build passed and the version command reported `2.0.0-alpha.4`.
- [x] Review the historical Linux ARM64 focused and Native AOT run (E3).
- Deferred to [#4](https://github.com/ferrywlto/ai-env-as-code/issues/4): real Codex and GitHub Copilot harness verification. These checks remain unverified.
- [x] Review the mechanical identifier-constants cleanup and independent literal-value contract test (G1).
- [x] Complete Debug/Release regression and review the verification matrix (H4). Debug passed 1,441/1,441; Release accounted for all 1,441 across the interrupted run and remaining batches. Ferry approved the split evidence and final matrix.
- [x] Accept alpha completion and defer the official release until Ferry's own migration verification and later request (I1).
- [x] Record alpha completion and close #2 (I2). Official release remains separate.

The latest target is 31 October–1 November 2026, not permission to waive gates.
The breaking provider-first CLI/flag upgrade is version 3 Backlog
[#3](https://github.com/ferrywlto/ai-env-as-code/issues/3), not a 2.0 blocker.
Keep existing command/flag forms in 2.0; public migration wiring is still required.

## Slice index

Reviewed means the recorded slice passed its scoped checks and Ferry review;
it does not certify the full release or automatically authorise the next slice.
13d/13e are open integration umbrellas, not additional unapproved scope.
Detailed prior results/approvals are linked, not repeated here.

| Slice | Outcome | State | Evidence |
| --- | --- | --- | --- |
| 1 | Codex config preflight | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 2 | Codex config/instruction writer | Reviewed | [2c8a604](https://github.com/ferrywlto/ai-env-as-code/commit/2c8a604) |
| 3 | Single-section backup preview | Reviewed | [cd5b6e7](https://github.com/ferrywlto/ai-env-as-code/commit/cd5b6e7) |
| 4 | Backup rejection boundaries | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 5 | Authored-source and target writing | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 6 | Exact-path backup commit | Reviewed | [76eb5be](https://github.com/ferrywlto/ai-env-as-code/commit/76eb5be) |
| 7 | Internal provider status | Reviewed | [668e655](https://github.com/ferrywlto/ai-env-as-code/commit/668e655) |
| 8 | Public Codex apply | Reviewed | [9ea08ab](https://github.com/ferrywlto/ai-env-as-code/commit/9ea08ab) |
| 9 | Public Copilot apply | Reviewed | [3c93214](https://github.com/ferrywlto/ai-env-as-code/commit/3c93214) |
| 10 | Public provider status | Reviewed | [d93be42](https://github.com/ferrywlto/ai-env-as-code/commit/d93be42) |
| 11 | Public Codex backup | Reviewed | [1dbcdd9](https://github.com/ferrywlto/ai-env-as-code/commit/1dbcdd9) |
| 12 | Public Copilot backup | Reviewed | [cfb86dd](https://github.com/ferrywlto/ai-env-as-code/commit/cfb86dd) |
| 13 | Pulled-repository enrollment and path confirmation | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13a | Legacy migration rehearsal | Reviewed | [1e967e6](https://github.com/ferrywlto/ai-env-as-code/commit/1e967e6) |
| 13b | Interactive migration contract | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13c | Data-format preflight | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d | Migration preparation (integration umbrella) | Integration open | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.1 | Local harness selection | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.2 | Dirty-section choices | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.3 | Remote/default-branch preflight | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.4 | Fetch-only preparation | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.5 | Guarded fast-forward | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.6 | Preparation coordinator | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.7 | Legacy capture preview | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.8 | Terminology glossary | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.9 | Legacy capture writer | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.10 | Legacy capture write/commit | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.11 | Portable runtime capture preview | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.12 | Single-provider capture | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13d.13 | Combined authored-source writer | Reviewed | [f99e2eb](https://github.com/ferrywlto/ai-env-as-code/commit/f99e2eb) |
| 13d.14 | Combined target writer | Reviewed | [7e4ba8e](https://github.com/ferrywlto/ai-env-as-code/commit/7e4ba8e) |
| 13d.15 | Combined capture/commit | Reviewed | [e41b3e1](https://github.com/ferrywlto/ai-env-as-code/commit/e41b3e1) |
| 13d.16 | Required scaffold preview | Reviewed | [bd1dbbb](https://github.com/ferrywlto/ai-env-as-code/commit/bd1dbbb) |
| 13d.17 | Missing-only scaffold writer | Reviewed | [7bbc46e](https://github.com/ferrywlto/ai-env-as-code/commit/7bbc46e) |
| 13d.18 | Scaffold validation performance | Reviewed | [7779a9a](https://github.com/ferrywlto/ai-env-as-code/commit/7779a9a) |
| 13d.19 | Overwrite preview | Reviewed | [758fd64](https://github.com/ferrywlto/ai-env-as-code/commit/758fd64) |
| 13d.20 | Reviewed scaffold reset writer | Reviewed | [14adaf3](https://github.com/ferrywlto/ai-env-as-code/commit/14adaf3) |
| 13e | Completion flow (integration umbrella) | Integration open | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 13e.1 | Exact-DONE prompt | Reviewed | [3b2a23a](https://github.com/ferrywlto/ai-env-as-code/commit/3b2a23a) |
| 13e.2 | Post-DONE source validation | Reviewed | [43f6a4e](https://github.com/ferrywlto/ai-env-as-code/commit/43f6a4e) |
| 13e.3 | Source-only commit | Reviewed | [e01b76b](https://github.com/ferrywlto/ai-env-as-code/commit/e01b76b) |
| 13e.4 | Single-harness target preview | Reviewed | [23560bd](https://github.com/ferrywlto/ai-env-as-code/commit/23560bd) |
| 13e.5 | Single-harness target writer | Reviewed | [7f7254a](https://github.com/ferrywlto/ai-env-as-code/commit/7f7254a) |
| 13e.6 | Single-harness target commit | Reviewed | [f9f6f7e](https://github.com/ferrywlto/ai-env-as-code/commit/f9f6f7e) |
| 13e.7 | Read-only migration apply preview | Reviewed | `f4717c6` + `4f1639e`; [117 passing checks](artifacts/migration-apply-preview-20261101/content-mode-fix/) |
| 13e.8 | Copilot single-file migration apply | Reviewed | `feb4641`; [55 passing checks](artifacts/migration-copilot-apply-20261101/). |
| 13e.9 | Codex two-file migration apply | Reviewed | `42c98be`; [51 passing checks](artifacts/migration-codex-apply-20261102/); diagrams and operating precondition in `a51eacf`. |
| 13e.10 | Enrolled-provider apply coordination | Reviewed | `4ca3ce2`; [44 passing checks](artifacts/migration-apply-coordinator-20261102/verification.md); all-provider review, ordered writes, no-op, stale inputs and partial-failure boundaries. |
| 13e.11 | Post-apply migration status gate | Reviewed | `55237f7`; [27 passing checks](artifacts/migration-status-gate-20261102/verification.md); read-only enrolled-provider/config checks, stale inputs and persistent drift refusal. |
| 13e.12 | Migration completion preview | Reviewed | `dd85526`; [21 passing checks](artifacts/migration-completion-preview-20261102/verification.md); fresh status, marker proposal, exact context observations and persistent-drift refusal. |
| 13e.13 | Completion marker creation | Reviewed | `bb4227c`; [104 passing checks](artifacts/migration-completion-writer-20261102/verification.md); private CreateNew marker, stale/competing input refusal and retained failure state. |
| 13e.14 | Completion marker commit | Reviewed | `603922e`; [26 passing checks](artifacts/migration-completion-committer-20261102/verification.md); exact publication, signing failure and retained phase drift. |
| 13e.15 | Post-DONE completion coordination | Reviewed | `4f4c2df`; [13 passing checks](artifacts/migration-post-done-20261102/verification.md). |
| 13e.16 | Guarded capture after preparation | Reviewed | 46/46 focused checks (7 coordinator, 39 committer regressions). Legacy and combined portable capture, unchanged inputs, mixed formats and stale HEAD. |
| 13e.17 | Capture to scaffold handoff | Reviewed | 2 handoff tests and 12 scaffold regressions passed. Next step is wiring the editing and `DONE` boundary. |
| 13e.18 | Editing to completion handoff | Reviewed | 16 focused tests passed, including first legacy migration through the prompt, EOF and cancellation. |
| 13e.19 | Public migration command | Reviewed | Release build passed with zero warnings or errors. All 15 focused tests passed, including public dispatch through DONE and EOF. Built CLI help and relative path rejection checked. |
| 13f | Full command rehearsal and native coverage | Actual Codex-copy and synthetic Copilot rehearsals reviewed (D2). macOS ARM64 smoke passed at `8599e59`; Linux ARM64 and Windows x64 passed at `d6af1ad` and were reviewed (E4). |
| 14 | Provider-first CLI contract | Deferred to v3 | [#3](https://github.com/ferrywlto/ai-env-as-code/issues/3) |
| 15 | Provider-first installer/skills/docs alignment | Deferred to v3 | [#3](https://github.com/ferrywlto/ai-env-as-code/issues/3) |
| 16 | Two-checkout Git round trip | Reviewed | [3d4f8e0](https://github.com/ferrywlto/ai-env-as-code/commit/3d4f8e0) |
| 17 | macOS ARM64 native lifecycle | Reviewed | [checkpoint](https://github.com/ferrywlto/ai-env-as-code/commit/db3ea38) |
| 18 | Windows x64 native lifecycle | Reviewed | [runner](https://github.com/ferrywlto/ai-env-as-code/actions/runs/40201089188) |
| 19 | Linux ARM64 native lifecycle | Passed and reviewed (E3) | [runner at `85fbd192`](https://github.com/ferrywlto/ai-env-as-code/actions/runs/40218058322) |
| 19a | Mechanical identifier-constant cleanup | Reviewed | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |
| 20 | Stable release acceptance | Open | [record](docs/history/aec-2-progress-before-compaction-2026-11-01.md#ticket-snapshot) |

## Retained follow-ups and historical uncertainties

| Item | Treatment |
| --- | --- |
| Top-level migration warning | Required when wiring the public flow: print once before its first mutation; require the operator to close affected harness sessions and pause other writers until completion. Separate terminal/plain editor; no automatic process detection or termination. Warning emission is not implemented by this documentation slice. |
| Migration check-ownership refactor | After the functional flow is complete, seek separate approval to consolidate overlapping full-context checks. Keep independently safe components, mutation/commit boundary protections, existing contracts and tests; no generic transaction framework. Deferred work, not implemented or credited as release acceptance here. |
| Real Windows/Linux Codex harness checks | Still unverified follow-ups; do not upgrade the support matrix using fixtures/native smoke alone. Existing platform requirements remain; this cleanup changes no scope. |
| Earlier five-commit semantic rewording | Historical unchecked item; reconcile with current history before claiming resolved. No rebase or automatic retry authorised here. |
| Formatting check after Roslyn named-pipe timeout | Historical unchecked item; recheck at an approved verification gate. |
| Earlier prerelease skill-packaging checkbox | Historical unchecked row is superseded by the recorded alpha.4 packaging completion; installed personal skill state is not inferred. |

## Record ownership

- Ticket: outcome, scope, DoD, current blocker and next action.
- This checklist: one short row per slice, latest state and evidence pointer.
- Chat, Git and test artifacts: approvals, failed attempts and detailed verification.
- Design docs: current contracts/decisions, not execution diaries.
- Update current rows in place; do not append a transcript for every approval or rerun.
- Commit only exact reviewed paths after verification and Ferry approval; push is separately authorised.

[Historical snapshot](docs/history/aec-2-progress-before-compaction-2026-11-01.md) preserves the pre-cleanup ticket and checklist,
including previous release history, evidence locations and intermediate failures.
It is reference-only, not a live progress index.
