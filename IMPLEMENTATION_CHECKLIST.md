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

### Full-suite diagnostic on 21 November 2026

- Ran the unfiltered Debug suite at `020f1bd` on macOS ARM64. The build
  passed with zero warnings or errors. The run used workspace-local
  fixtures, Git Trace2, and a five-minute diagnostic hang guard. The
  documented pre-push command uses a two-minute guard.
- Stopped at Ferry's cutoff after 34m 52s. The console logged 570 passes,
  zero failures, and zero skips. The cancellation produced no TRX or
  final suite result. The test process tree exited and its console and
  trace are under `artifacts/full-regression.6bEZPE`.
- The previously interrupted public recovery case passed in 3m 45s.
  The original case reached the five-minute watchdog while Git was still
  active. The current case still exceeds the documented two-minute guard.
- Git Trace2 logged 52,660 starts, including 14,176 `rev-parse`,
  7,420 `ls-tree`, and 5,518 `cat-file`. Regression follow-up cases
  accounted for 24m 14s of rounded test durations. The 13 target-drift
  cases took 5m 08s, and the five injected post-DONE failure cases took
  3m 59s.
- The 529 exact test display names in both interrupted runs took
  20m 24s before and 28m 07s in this run, summing rounded console case
  durations. This shared subset ran slower. Neither run completed, so
  the full-suite runtime and percentage improvement remain unverified.

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
in 125.28 s. Ferry accepted the source-writer cleanup on 20 November 2026
(`1b8b88b`).

### Migration apply argument checks and shared history fixture

- Moved eight argument-rejection cases from Git fixtures to in-memory
  reviews: four invalid coordinator review sets, three incompatible writer
  reviews, and one unsupported preview provider. The empty review case
  joins them in the unit group. The unit tests assert the specific rejection
  reasons. Real-Git tests retain missing enrollment, stale inputs, both
  provider writes, write ordering, and partial failure.
- The shared migration fixture seeds the recognized two-commit Codex
  history directly, preserving the root tree, initialized instruction
  bytes, unchanged config, notes, optional Copilot commit, and initial
  remote refs. Tests that actually advance the remote get one writer
  checkout on demand. Dedicated initialization tests still exercise skill
  installation and the complete `aec init` workflow.
- Matched 19 cases across apply rejection, local capture preview, and real
  remote advancement on macOS ARM64. The unsupported preview provider
  moved separately and is covered by the full fast suite. Both
  measurements used a prior Debug build, TRX, workspace fixtures, and
  Git Trace2.

| Measurement | Before | After |
| --- | ---: | ---: |
| Passing cases | 19 | 19 |
| Git process starts | 6,512 | 2,109 |
| Writer checkout clones | 18 | 6 |
| Wall time | 85.55 s | 39.74 s |

All 196 unit tests passed with zero Git calls. All seven real-Git checks
passed in 216.93 s, including the focused migration group, two-provider
apply, and public retry. Ferry accepted the argument and fixture changes
and their focused push gate on 20 November 2026.

### Scaffold preview coverage

- Removed 11 repeated Git fixture runs. The in-memory scaffold policy
  tests cover the full choice matrix and when staging blocks a proposal.
- The preview suite keeps one real-Git case for each dirty state in the
  former Preserve and Overwrite matrices, plus clean sources under
  Overwrite. It still checks that preview leaves the working tree and index
  untouched. The hidden-edit preview and the writer's no-choice case
  cover the explicit-choice requirement.
- The retained dirty cases compare against known fixture bytes and avoid
  temporary writes unrelated to the Git state under test.

Both preview runs used a prior Debug build, identical repository-local
fixture settings, TRX, and Git Trace2 on macOS ARM64.

| Preview measurement | Before | After |
| --- | ---: | ---: |
| Passing cases | 34 | 23 |
| Git process starts | 6,984 | 4,913 |
| Wall time | 154.57 s | 105.17 s |

All 49 scaffold writer tests passed in 307.41 s. All 196 unit tests passed
in 1.01 s wall time with zero Git process starts. Ferry accepted the
scaffold preview cleanup on 20 November 2026. Full-suite verification
remains pending for #19.

### Portable target writer rejection coverage

- Moved four malformed target path variants to Git-free enrollment tests.
  One real-Git preparation test still verifies the rejection and exact
  diagnostic for an invalid committed target.
- The stale-input and non-local preparation fixtures seed one non-local
  Codex enrollment. Stale-input tests still enroll both local providers.
  Consumer-selection and commit tests still seed the full platform matrix.
- Preparation tests build the review once, on the side of the mutation
  needed for each guard. They check the specific rejection diagnostic and
  preserve the entire fixture.

Both timed runs used a prior Debug build, identical repository-local
fixture settings, TRX, and Git Trace2 on macOS ARM64.

| Rejection benchmark | Before | After |
| --- | ---: | ---: |
| Real-Git cases executed | 22 | 19 |
| Git process starts | 14,274 | 10,182 |
| Observed wall time | 508.90 s | 365.28 s |

Three revised timing cases initially asserted the writer's baseline
diagnostic. The earlier migration dirty-path guard rejects those changes.
The assertions were corrected to match that guard. The 16 cases that
passed in both timing runs took 385.70 s before and 335.32 s after,
summed from their TRX case durations.

The follow-up passed all 24 cases in 469.25 s, including all five
non-local preparation cases, the remaining target-writer suite, and
three cross-platform commit checks. Together the two runs passed all 38
distinct real-Git checks in scope. All 200 unit tests passed in 1.30 s
wall time with zero Git starts. Ferry accepted the cleanup on
20 November 2026. Full-suite verification remains pending for #19.

### Portable capture preview rejection coverage

- Removed three Git fixture runs. Legacy capture preview tests exercise
  the shared original-commit ID and ancestry guard. The in-memory
  attribution suite checks edits to multiple sections for both providers.
- The seven retained rejection tests use one portable Codex provider.
  They assert the rejection reason and preserve the whole fixture.
  Separate tests still exercise two portable providers and conflicting
  shared proposals.
- Missing-target, old-digest, and dirty-draft tests create their review
  once. The old-digest test keeps the original commit pinned and updates
  only the committed shared source and Codex target.

Both preview runs used a prior Debug build, repository-local fixtures,
TRX, and Git Trace2 on macOS ARM64.

| Preview measurement | Before | After |
| --- | ---: | ---: |
| Passing cases | 18 | 15 |
| Git process starts | 3,231 | 2,711 |
| Observed wall time | 74.41 s | 36.37 s |

The eight untouched cases also ran faster, falling from 41.35 s to
23.72 s in summed TRX durations. The reduction of 520 Git starts
measures the fixture savings directly.

All four legacy preview history checks and all 200 unit tests passed.
The unit run took 0.78 s with zero Git starts. Ferry accepted the preview
cleanup on 20 November 2026. Full-suite verification remains pending for
#19.

### Portable capture committer diagnostic

- All 12 stale-input cases passed in 169.00 s using the existing Debug
  build, workspace-local fixtures, the two-minute per-test hang guard,
  and a ten-minute run cap. Git Trace2 recorded 5,487 process starts.
- Git launched 1,446 `rev-parse`, 859 `ls-tree`, and 840
  `cat-file` commands. Together they accounted for 57% of starts.
  An initial review and a fresh review repeat the Git reads before the
  committer compares the two typed reviews.
- The duplicate peer-runtime case and two caller-review mutation cases
  each started 534 Git processes.

### Portable capture stale-input coverage

- Extracted the existing typed review comparison from the fresh-preview
  workflow. Nine in-memory cases exercise matching reviews and changes to
  branch, provider presence, target bytes, runtime bytes, and config.
- Kept real checkout drift checks and a representative forged review
  that the committer must reject before writing. Removed the duplicate
  peer-runtime row and moved forged target bytes to the unit suite.
  Eight retained Git cases now seed one portable provider.

Both runs used the same stale-input test filter, Debug build settings,
workspace fixtures, Git Trace2, and hang limits.

| Stale-input measurement | Before | After |
| --- | ---: | ---: |
| Passing real-Git cases | 12 | 10 |
| Git process starts | 5,487 | 3,792 |
| Wall time | 169.00 s | 121.95 s |

All 209 unit tests passed in 1.54 s with zero Git starts. Both focused
committer cases passed in 47.15 s, covering recomputed proposals and
staging preservation on signing failure. Ferry accepted the stale-input
cleanup on 20 November 2026. Full-suite verification remains pending for
#19.

### Consolidate stale-input boundary coverage

Removed three committer fixture runs with coverage at the same guards.

| Removed committer case | Retained coverage |
| --- | --- |
| Changed HEAD | Portable capture preview checks the same preflight comparison and verifies the fixture is unchanged. |
| Uncommitted marker | Migration preflight checks unstaged and staged markers and verifies no changes. The public recovery case covers refusal and a successful retry. |
| Forged provider presence | The in-memory review comparison checks the forged provider. The committer's peer-runtime case exercises that comparison and verifies the fixture is unchanged. |

The same Debug filter, workspace fixtures, Git Trace2, and hang limits
were used for all three runs.

| Committer stale-input measurement | Original | Before this cleanup | After |
| --- | ---: | ---: | ---: |
| Passing real-Git cases | 12 | 10 | 7 |
| Git process starts | 5,487 | 3,792 | 2,686 |
| Wall time | 169.00 s | 121.95 s | 84.35 s |

All nine retained preview and marker guard cases passed in 49.24 s. All
209 unit tests passed in 1.86 s with zero Git starts. Ferry accepted
the boundary coverage cleanup on 20 November 2026. Full-suite verification
remains pending for #19.

### Scaffold writer stale-state coverage

- Ten Git-free tests check equivalent inventories, branch and membership,
  and changes to individual file identity, bytes, and actions. One
  real-Git forged-path test proves the writer rejects the inventory
  before changes.
- Removed five writer cases that reached the same fresh-preview guards
  already exercised by the changed HEAD, enrollment, dirty-path, marker,
  and ignored-source preview cases. Retained real branch, draft-byte, and
  index changes that reach the inventory comparison.
- Consolidated three between-write legacy-file cases into one fixture.
  Git status hides each edit, so the test now reaches the protected-byte
  guard and checks its specific rejection. The unrelated-dirt case still
  exercises the dirty-path guard.

Both runs used Debug builds, workspace fixtures, TRX, Git Trace2,
and the same hang limits. The revised filter includes the replacement
legacy-input case.

| Scaffold writer measurement | Before | After |
| --- | ---: | ---: |
| Passing real-Git cases | 28 | 18 |
| Git process starts | 6,365 | 3,978 |
| Observed wall time | 123.30 s | 97.87 s |

The 16 cases common to both runs took 66.41 s before and 83.78 s after,
summed from their TRX durations. Git starts measure the reduced fixture
work directly.

All nine focused preview and writer checks passed in 29.40 s. All
219 unit tests passed in 1.24 s with zero Git starts. Ferry accepted
the scaffold writer cleanup on 20 November 2026. Full-suite verification
remains pending for #19.

### Shared migration remote fixture

- Local-only migration fixtures skip creating a bare remote, configuring
  an upstream, and pushing the seed branch. They retain real local
  history, index, working files, and provider runtimes.
- Tests that exercise remote operations, assert remote or tracking refs,
  or use an unreachable remote to check refusal ordering request a
  remote explicitly. Public migration and recovery keep their upstream.

The same 31-case filter covered scaffold and legacy capture previews,
fast-forward, and portable capture. Both runs used a prior Debug build,
workspace fixtures, TRX, Git Trace2, and the same hang limits.

| Shared fixture measurement | Before | After |
| --- | ---: | ---: |
| Passing cases | 31 | 31 |
| Bare remote initializations and initial pushes | 31 each | 4 each |
| Git process starts | 6,775 | 6,532 |
| Observed wall time | 203.56 s | 256.59 s |

The 27 skipped remotes removed 243 Git starts. The four cases that
retained a remote also ran slower, with summed TRX durations of
44.60 s before and 54.14 s after. This sample verifies less Git work.
The wall-time benefit remains unverified.

All 11 focused remote contract cases passed in 402.23 s, including
remote-backed preparation, source and target commits, first migration,
and public retry. That run used a five-minute per-test hang guard for the
public retry, which took 3m 18s. All 219 unit tests passed in 1.64 s with
zero Git starts. Ferry accepted the fixture cleanup on 21 November 2026.
Full-suite verification remains pending for #19.

### Regression follow-up Git process reduction

- Portable target drift tests exercise the existing runtime and
  canonical byte guards with typed in-memory inputs. Seven real-Git
  cases retain HEAD, branch, staged and raw index, marker, peer runtime,
  and non-local source and target observation. The two non-local
  variations share one prepared fixture and check separate diagnostics.
- Five in-memory post-DONE cases exercise phase order, failure cutoff,
  and the pinned HEAD diagnostic through the coordinator. One real-Git
  failure case verifies that the two prior commits and the fixture stay
  intact and the completion marker is absent.

Both runs used the same two-method Debug filter, repository-local
fixtures, TRX, Git Trace2, and the two-minute per-test hang guard.

| Regression follow-up measurement | Before | After |
| --- | ---: | ---: |
| Passing real-Git cases | 18 | 8 |
| Git process starts | 13,771 | 4,031 |
| Wall time | 384.48 s | 126.97 s |

The bounded workload removed 9,740 Git starts (70.7%). Starts within
the target-drift case intervals fell from 7,808 to 3,435. Starts within
the post-DONE failure case intervals fell from 5,962 to 595. The
52,660-start interrupted full run remains the overall diagnostic baseline.

All 236 unit tests passed in 1.58 s with zero Git starts. The first
legacy migration and combined portable capture checks both passed in
104.10 s with a three-minute per-test guard. Ferry accepted the cleanup
on 21 November 2026. Full-suite verification remains pending for #19.

### Target and legacy review comparison coverage

- Typed unit tests exercise target review comparisons, including the
  selected and required source inventories. Four writer cases retain
  real branch, staged index, peer overlay, and forged target path
  checks. Three use only the selected Codex harness.
- Target HEAD, original commit, ignored target, and enrollment checks
  remain in target preview, source validation, or target committer
  suites exercising the same fresh-preview guards.
- Typed unit tests exercise legacy review branch, file membership,
  path, and byte comparisons. One real capture committer case rejects
  a forged proposal before writing or staging. The legacy writer's
  existing stale-input matrix covers the six removed checkout and
  runtime mutations with specific diagnostics.

Both runs used the same two-method Debug filter, workspace fixtures,
TRX, Git Trace2, and the two-minute per-test hang guard.

| Target and legacy review measurement | Before | After |
| --- | ---: | ---: |
| Passing real-Git cases | 24 | 5 |
| Git process starts | 6,972 | 1,408 |
| Wall time | 122.42 s | 20.99 s |

The bounded workload removed 5,564 Git starts (79.8%). This and the
separate target-drift and post-DONE workload removed 15,304 Git starts
across their measured cases. Full-suite Git savings remain unverified.

All 23 focused guard and commit cases passed in 107.84 s. All 260
unit tests passed in 1.21 s with zero Git starts. Ferry accepted the
review comparison cleanup on 21 November 2026.

### Apply coordinator partial-failure coverage

- Three in-memory cases exercise completed-runtime drift, committed
  target drift, and an exception after the first provider. They run the
  coordinator's provider loop and protected-byte comparison and verify
  the completed result, pinned inputs, and skipped second writer.
- Two real-Git cases retain peer-runtime and authored-source drift.
  They assert separate guard diagnostics, preserve the first provider's
  applied bytes, and check that the second provider never writes.

Both runs used the same Debug filter, workspace fixtures, TRX, Git
Trace2, and the two-minute per-test hang guard.

| Apply failure measurement | Before | After |
| --- | ---: | ---: |
| Passing real-Git cases | 5 | 2 |
| Git process starts | 4,503 | 1,804 |
| Observed wall time | 63.80 s | 37.94 s |

The two retained cases ran slower in the second pass (25.23 s to
37.12 s in summed TRX durations). Git starts show the removed fixture
work directly. The three targeted regression workloads together
removed 18,003 measured Git starts. Full-suite verification remains
pending for #19.

All five additional coordinator checks passed in 54.46 s. All 263 unit
tests passed in 1.27 s with zero Git starts. Ferry accepted the
apply-coordinator cleanup on 21 November 2026.

### Public migration recovery fixture

- The recovery test enrolls only Codex and still uses a real remote.
  It verifies the CLI refuses an ignored uncommitted marker before
  prompting, then retries, keeps the prior HEAD as the new commit's
  parent, commits only the marker, and preserves runtime bytes and a
  clean checkout.
- The separate migration completion tests still cover real
  two-provider ordering and marker-only completion.

Both runs used the same one-test Debug filter, workspace fixture
settings, TRX, and Git Trace2.

| Public recovery measurement | Two providers | One provider |
| --- | ---: | ---: |
| Passing cases | 1 | 1 |
| Git process starts | 6,457 | 4,661 |
| Wall time | 128.78 s | 68.97 s |
| Per-test hang guard | 5m | 2m |

The revised case passed under the documented two-minute guard with
1,796 fewer Git starts (27.8%). The four targeted regression workloads
together removed 19,799 measured starts. All 263 unit tests passed in
1.17 s with zero Git starts. Ferry accepted the recovery fixture and
routine suite split on 21 November 2026.

### Routine regression and legacy migration

- The documented routine Debug run uses `Category!=LegacyMigration`.
  The separate `Category=LegacyMigration` run covers the real-Git
  1.x to 2.0 workflow. The unfiltered Release run includes both.
- Discovery found 840 routine cases and 627 legacy cases. The sets
  are disjoint and cover all 1,467 discovered cases. All five
  `MigrationReview` cases are in the legacy group.
- The routine selection keeps Git-free migration decisions, the
  general help check, and current lifecycle and two-checkout cases.

The routine Debug run passed all 840 cases in 678.88 s (11m 19s)
with the two-minute per-test hang guard. The TRX exactly matched
routine discovery and included zero legacy cases. Git Trace2
recorded 38,385 starts. This is the first completed routine baseline
with the new selection.

All 263 unit cases passed in 1.22 s with zero Git starts. Three
opt-in legacy checks passed in 7.09 s, including staged and unstaged
marker refusal and the case tagged for both migration categories.
The full opt-in legacy group and unfiltered Release run remain
unverified for this checkpoint.

### Routine initialization and enrollment

Replaced full Codex initialization with minimal worktrees or directly
seeded history when another command is under test. Retained real Git
for history, clone and checkout binding, and real files for runtime
writes, collisions, and link handling. Eight Codex skill installer
checks now call the installer directly with a scratch directory.
Eleven argument and binding cases are in the Git-free unit group.
The older Codex block versions remain in the in-memory merge tests.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 129 | 127 |
| Cases that started Git | 121 | 96 |
| Git Trace2 process starts | 17,549 | 9,024 |
| Wall time | 349.98 s | 215.27 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. The revised filter includes the cases moved to unit classes.
The 274-case unit group passed in 1.24 s with a failing `git` shim that
recorded zero calls. Seven focused legacy migration cases and both
two-checkout Git round trips passed in 60.38 s. The full routine figure
above predates these changes.

Ferry accepted the initialization and enrollment slice.

### Shared target status and preview

- The in-memory apply/status suite exercises block upgrades, including
  Codex ChatGPT guidance, and missing or wrong-platform bindings.
  The in-memory backup suite exercises malformed and unstamped blocks
  and the source writer's missing-attribution guard.
- Public status, apply, and backup tests cover normal checkout reads,
  dirty targets, and refusal without writes. Direct writer tests keep
  real Git and filesystem checks for stale reviews, changed HEAD,
  write order, and partial updates.
- Removed the unused pre-cutover `SharedTargetStatus` adapter.
  The active status path uses `PortableEnvironmentStatus`.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases in `SharedTargetStatusTests` | 92 | 27 |
| Cases that started Git | 92 | 26 |
| Git Trace2 process starts | 5,005 | 1,807 |
| Wall time | 88.22 s | 36.49 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. The full unit group grew from 274 to 295 cases and passed in
1.16 s with zero calls to the failing `git` shim. All 67 focused
CLI and committed-target boundary cases passed in 64.75 s.
The initialization and status groups together saved 11,723 Git starts
in focused runs. The full routine suite has not been rerun.

Ferry accepted the shared target status and preview slice.

### Portable backup target writer and committer

The fixtures seed the reviewed runtime and authored source directly.
Committer cases also seed the expected rendered bytes. The production
target writer and committer still validate real enrollments, HEAD,
runtime and source changes, rendered bytes, and the index. Tests for
local refusals use a single enrollment. Scope and missing-enrollment
checks keep their cross-platform fixtures.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 18 | 18 |
| Git Trace2 process starts | 3,979 | 863 |
| Wall time | 56.89 s | 21.28 s |

Both runs used prebuilt binaries, the same filter, and the two-minute
per-test hang guard. All 25 public Codex and Copilot backup cases
passed in 29.21 s. All 295 unit cases passed in 1.18 s with zero calls
to the failing `git` shim.

The first three routine cleanup slices saved 14,839 Git starts across
non-overlapping focused runs.

Ferry accepted the backup target writer and committer slice.

### Codex completed-checkout attachment and history

Seeded the ordinary two-commit history and optional ChatGPT upgrade
for 13 attachment, rebinding, and history-refusal cases. Missing-runtime
fixtures start with empty homes. Clones, moves, altered commits, and
the production CLI checks remain real. Partial-resume tests still
exercise a CLI-generated completion followed by a second attachment.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 13 | 13 |
| Git Trace2 process starts | 2,682 | 922 |
| Wall time | 57.71 s | 20.97 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. The renamed divergent-runtime test replaces its original row
in the after filter. All 295 unit cases passed in 1.17 s with zero Git
calls. All 20 additional fresh initialization, resume, enrollment,
ChatGPT scaffold, clone, and migration checks passed.

This saves a further 1,760 Git starts after the
initialization and enrollment checkpoint, bringing the measured
routine slice savings to 16,599 starts. The full routine suite has
yet to be rerun after these changes.

Ferry accepted the completed-checkout attachment and history slice.

### Codex partial initialization and recovery

The shared fixture seeds the legacy and managed baseline commits
directly. Legacy completion cases also seed their second commit.
The tests retain the real CLI resume after a Git signing failure,
history and metadata refusals, and concurrent file changes.
An exact two-entry Git log covers the commit count and subjects.
The check that public status refuses an unenrolled checkout stays
with fresh initialization.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 18 | 18 |
| Git Trace2 process starts | 1,428 | 1,176 |
| Wall time | 20.11 s | 23.73 s |

The matched runs used prebuilt binaries and the two-minute per-test
hang guard. The 252 fewer starts bring measured routine slice savings
to 16,851. The timing pair showed no wall-clock savings for this slice.
All 295 unit cases passed in 1.15 s with zero Git calls. All 10 fresh
initialization, attachment, enrollment, and migration checks passed.

Ferry accepted the partial initialization and recovery slice.

### Two-provider Git round trip

Seeded the first checkout's committed canonical history, authored
sources, enrolled target, and local runtime for both providers.
The tests still render and publish a subsequent change through a real
local bare remote. The second checkout runs explicit rebind, checks
that status and apply leave its old HEAD and remote-tracking ref
untouched, then fetches, fast-forwards, and applies the new target.
Both checkouts retain their own runtime binding and local config.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 2 | 2 |
| Git Trace2 process starts | 2,174 | 1,294 |
| Wall time | 49.86 s | 26.34 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 295 unit cases passed in 1.17 s with zero Git calls.
All 10 focused enrollment and apply cases passed in 25.10 s.
The 880 fewer starts bring measured routine slice savings to 17,731.
The full routine suite has yet to be rerun.

Ferry accepted the two-provider Git round trip slice.

### Fresh Codex initialization overlap

The fresh checkout test verifies the default provider, absence of a
ChatGPT scaffold, and installed skill contents. Existing in-memory
rows verify older block replacement. A new row verifies replacement
of a stale version-5 template with exact surrounding bytes.
The real Git cases still verify the exact CRLF baseline bytes and
an empty second commit for an already managed runtime.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing integration cases | 8 | 4 |
| Git Trace2 process starts | 1,164 | 594 |
| Wall time | 23.85 s | 13.25 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 296 unit cases passed in 1.16 s with zero Git calls.
All eight focused configuration, attachment, and resume cases passed.
The 570 fewer starts bring measured routine slice savings to 18,301.
The full routine suite has yet to be rerun.

Ferry accepted the fresh Codex initialization overlap slice.

### Five-minute routine suite profile

The prebuilt Debug run used `Category!=LegacyMigration` and the
documented two-minute per-test hang guard. It discovered 791 cases.
The previous completed routine baseline discovered and passed 840
cases in 678.88 s with 38,385 Git starts.

| Elapsed | Passed | Git starts |
| --- | ---: | ---: |
| 1 min | 433 | 3,829 |
| 2 min | 507 | 7,689 |
| 3 min | 613 | 11,539 |
| 4 min | 658 | 15,411 |
| 5 min | 763 | 19,407 |

No failures or skips were reported. The runner stopped its process
group at the five-minute cap, and a process check confirmed it had
exited. All 28 cases without a result were in `ApplyTests`. The
stopped run did not produce a final TRX and does not give a complete
suite duration or Git count. The artifacts include the raw Git trace,
discovery, console output, and results reported before the cap.

Git starts attributed from Trace2 worktree paths were highest in the
shared portable status, apply, and backup fixtures (7,120), followed
by Codex initialization (3,813) and Copilot initialization (1,552).
`rev-parse` accounted for 6,389 starts across the profile.

Ferry accepted the five-minute routine profile and portable apply/status
cleanup.

### Portable apply and status fixtures

- Four apply/status option cases now exercise the CLI parser without
  fixtures. The real path-containment guard uses one scratch directory
  and preserves a runtime sentinel.
- Apply tests derive expected bytes from fixture content. The public
  Copilot apply check also verifies unrelated checkout bytes and status.
- Public status cases cover Codex config matching, drift and absence,
  committed config provenance, and Copilot home selection and ignored
  config. These replace overlapping calls to the internal status adapter.
  Committed target and runtime write checks retain real Git.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing integration cases | 51 | 38 |
| Cases that started Git | 51 | 37 |
| Git Trace2 process starts | 2,421 | 1,936 |
| Wall time | 52.67 s | 42.43 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 300 unit cases passed in 1.22 s with zero Git calls.
All 46 portable review, writer, and committed-target boundary cases
passed. The 485 fewer starts bring measured routine slice savings
to 18,786. The five-minute profile predates this slice.

Ferry accepted the portable apply and status fixture slice.

### Legacy apply and initialization fixtures

- A representative TOML table conflict still checks that neither
  runtime file is changed. The table syntax variants and runtime lone
  carriage returns are checked in memory. Version and filesystem
  root comparisons also run in the unit group.
- The early runtime containment and link refusals use scratch
  directories. The fixtures omit missing runtime config instead of
  creating and deleting it, and the Codex home selection test uses
  the original fixture bytes for its expected output.
- The shared test collection supplies Git identity and signing
  defaults. One local config write preserves fixture line endings,
  hook isolation, and empty global attributes. Real Git still covers
  committed bytes, staged index state, and recovery checkpoints.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases in `ApplyTests` | 46 | 38 |
| Cases that started Git | 42 | 36 |
| Git Trace2 process starts | 1,183 | 837 |
| Wall time | 21.06 s | 15.84 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 309 unit cases passed in 1.28 s with zero Git calls.
All 17 focused portable apply, recovery, and committed-target checks
passed. The 346 fewer starts bring measured routine slice savings
to 19,132. The earlier five-minute profile predates both apply slices.

Ferry accepted the legacy apply and initialization fixture slice.

### Second five-minute routine suite profile

The same prebuilt Debug selection and two-minute per-test hang guard
discovered 783 routine cases after the apply slices.

| Elapsed | Passed | Git starts |
| --- | ---: | ---: |
| 1 min | 404 | 1,818 |
| 2 min | 433 | 3,449 |
| 3 min | 466 | 5,062 |
| 4 min | 501 | 6,641 |
| 5 min | 521 | 8,208 |

The cap stopped the runner after 300.04 s. The process group was
confirmed gone. No failures or skips were reported, and 262 cases
had no result, including all 38 `ApplyTests` cases. The run produced
no final TRX. The raw trace and progress are in
`artifacts/routine-profile-capped.0dOVmy`.

The first capped run passed 763 of 791 in the same time budget.
The unchanged 38-case `StatusTests` group made 922 Git starts in
both runs. Its reported test durations summed to 14.26 s in the
first run and 27.17 s in the second. The second cap recorded fewer
starts while completing fewer cases. Full routine timing and total
Git starts remain unverified for the current checkout.

The shared portable fixtures accounted for 6,422 starts in the
second profile. Ferry accepted the portable backup overlap slice.

### Portable backup CLI, preview, and writer coverage

The public CLI cases verify the exact commit, rendered target,
unchanged runtime, and home selection. They also check both forms of
missing runtime personality and multi-section refusal. The Codex
preview retains the combined read-only plan and dirty committed
config checks. Direct writer tests retain revalidation after review,
no-change refusal, and independent branch and index guards.
Unsafe paths use disposable directories and sentinel bytes without
initializing Git.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 45 | 33 |
| Cases that started Git | 45 | 30 |
| Git Trace2 process starts | 2,679 | 1,718 |
| Wall time | 70.46 s | 58.37 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 309 unit cases passed in 1.52 s with zero Git calls.
All 30 portable review, target writer, and committer cases passed.
The 961 fewer starts bring measured routine slice savings to 20,093.
The capped routine profiles predate this slice.

Ferry accepted the portable backup overlap slice.

### Legacy Codex backup and initialization capture

The fixtures seed committed baselines directly when setup was the
only reason to invoke backup. The tests exercise the initializer's
capture entry point and the shared commit helper. First and config-only
commits retain their exact path and byte checks. Standalone legacy
missing-config warning tests are covered by current portable CLI tests;
initialization retains its own default-personality coverage.

Interrupted backup, retry, hooks, staged blobs, replacement trees,
and raw Git byte checks still run against real repositories. Git
identity and signing come from the shared test collection. The
fixture uses one local config write for hooks and line endings.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 28 | 24 |
| Git Trace2 process starts | 1,320 | 646 |
| Wall time | 39.74 s | 20.77 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 309 unit cases passed in 1.59 s with zero Git calls.
All 17 focused initialization and commit cases passed. The 674 fewer
starts bring measured routine slice savings to 20,767. The capped
routine profiles predate this slice.

Ferry accepted the legacy Codex backup fixture slice.

### Codex status options, syntax, and filesystem checks

Option checks and config syntax variants run in memory. The four
early path refusals use scratch directories without Git. The real
checkout tests still exercise leaf symlinks, size limits, committed
config refusal, independent runtime absence, and exact CRLF drift
without writes. Home selection and explicit-path precedence remain
through the CLI.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases in `StatusTests` | 38 | 15 |
| Cases that started Git | 35 | 11 |
| Git Trace2 process starts | 922 | 267 |
| Wall time | 27.44 s | 9.98 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 326 unit cases passed in 1.75 s with zero Git calls.
All 16 focused portable status and config provenance cases passed.
The 655 fewer starts bring measured routine slice savings to 21,422.
The capped routine profiles predate this slice.

Ferry accepted the Codex status options and syntax slice.

### Copilot checkout attachment fixtures

The first checkout's committed Codex history, canonical Copilot
instructions, and optional portable target are seeded directly.
The tests still clone, rebind, and refuse dirty or incorrectly bound
checkouts through the CLI. The implicit-home conflict checks use
local bindings and scratch directories without Git. Fresh Copilot
initialization and repeated installation tests retain normal CLI setup.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 18 | 18 |
| Cases that started Git | 18 | 16 |
| Git Trace2 process starts | 1,558 | 461 |
| Wall time | 43.74 s | 11.36 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 326 unit cases passed in 1.43 s with zero Git calls.
All five focused enrollment and two-checkout round-trip cases passed.
The 1,097 fewer starts bring measured routine slice savings to 22,519.
The capped routine profiles predate this slice.

Ferry accepted the Copilot checkout attachment slice.

### Direct portable writer review fixtures

Direct writer cases build reviewed snapshots from fixture bytes
and the real HEAD. The writers still revalidate the checkout and
runtime at their real write boundaries. Real preview cases retain
committed reads and config provenance. Stale-input cases assert the
specific diagnostic before checking the files stayed intact.

| Focused Debug run | Before | After |
| --- | ---: | ---: |
| Passing cases | 27 | 27 |
| Git Trace2 process starts | 1,807 | 1,273 |
| Wall time | 55.39 s | 44.55 s |

Both runs used prebuilt binaries and the two-minute per-test hang
guard. All 326 unit cases passed in 1.64 s with zero Git calls.
All 30 portable review, target writer, and committer cases passed.
The 534 fewer starts bring measured routine slice savings to 23,053.
The capped routine profiles predate this slice.

Ferry accepted the direct portable writer review fixture slice.

### Grouped routine verification

The prebuilt Debug selection uses `Category!=LegacyMigration` and
the two-minute per-test hang guard. Each group runs alone with a
five-minute total cap. Discovery found 761 cases across six disjoint
groups. Exact test names, including theory rows, are compared against
each group's TRX and the complete routine discovery list.

| Group | Passed | Git starts | Wall time |
| --- | ---: | ---: | ---: |
| Unit | 326 | 0 | 1.68 s |
| Codex initialization | 80 | 4,450 | 92.87 s |
| Provider lifecycle | 49 | 2,742 | 82.23 s |
| Portable apply and status | 65 | 3,209 | 106.37 s |
| Portable backup | 51 | 2,581 | 87.79 s |
| Other integration | 190 | 2,327 | 83.95 s |
| Total | 761 | 15,309 | 454.89 s |

All 761 cases passed, with zero failures or skips. Every TRX matched
its group discovery, and the result union matched routine discovery.
All groups completed below their cap. The logs, discovery lists, TRX
files and Git traces are in `artifacts/routine-grouped.XZFYwv`.

The prior completed routine baseline passed 840 cases with
38,385 Git starts. The revised routine selection used 23,076 fewer
starts (60.1%). The grouped wall times add to 7m 35s. The current
single-invocation routine duration remains unverified.

By TRX test windows, the largest remaining class was `InitTests`
with 3,874 starts, then `GitRoundTripTests` with 1,282 and
`SharedTargetStatusTests` with 1,271.

### Five-minute routine regression

The three follow-up changes preserve Git checks at repository boundaries
and reuse data within a command while it is still current.

| Change | Focused coverage | Git starts in the same test windows |
| --- | ---: | ---: |
| Batch the four committed portable inputs, including staged changes, raw bytes, and file modes | 128 passed | 4,501 → 3,330 |
| Reuse validated input snapshots and the first-parent history pinned to the inspected commit | 108 passed | 7,796 → 6,134 across batching and revalidation |
| Trim repeated reads from the Codex and Copilot transport scenarios | 2 passed | 742 → 620 after revalidation |

The transport cases still exercise push, clone, fetch, fast-forward, and
apply for both providers. Status verifies the pre-fetch and post-merge
states. The final checks compare local runtime and committed content.

The single prebuilt Debug run selected `Category!=LegacyMigration`
with a two-minute per-test hang guard and a five-minute process cap.
All 761 cases passed in 294.01 s, with zero failures or skips. The TRX
matched discovery case for case. The run recorded 12,118 Git starts,
3,191 fewer (20.8%) than the previous six-group run of the same 761
cases. The prior grouped wall times totalled 454.89 s. Build and
discovery were outside the timed test run. Logs, minute checkpoints,
TRX, and Git trace are in `artifacts/five-minute-acceptance.MHRZ09`.

### Batched Git metadata and initialization inputs

Apply, backup, and ChatGPT init use one Git query for worktree, bare,
and root metadata. Backup resolves seven operation paths in one query
and checks each sentinel in the original order. The batch falls back
to individual lookups if output cannot be mapped to the requested paths.
All 111 focused cases passed. Across the 110 matching cases, Git starts
fell from 2,754 to 2,291. The new case checks that an active sequencer
blocks backup before a write.

Invalid initialization arguments and provider-home selection have
fixture-free unit coverage. Conflicting implicit Codex and Copilot
bindings still use runtime instruction files, with no Git fixture.
Successful initialization, commit and recovery checks retain Git.
All 340 unit cases passed in 1.28 s with zero Git starts.

The single prebuilt Debug routine run (`Category!=LegacyMigration`)
passed all 762 discovered cases in 207.80 s, with zero failures or
skips. The TRX matched discovery. Git starts fell from 12,118 to
10,092 (2,026 fewer, 16.7%). The previous routine run took 294.01 s.
The new run finished 92 starts above 10,000. Logs, TRX, and Git trace
are in `artifacts/five-minute-metadata-routine.SFGobM`.

### Reuse the pinned commit in portable runtime checks

The previous trace showed 203 portable target reads with an adjacent
pair of initial HEAD reads. The target validator now returns the exact
commit alongside the validated target bytes. The runtime snapshot
reuses that commit. Both HEAD checks after reading target and runtime
content remain in place.

All 66 focused cases passed in 35.43 s, including stale HEAD refusal
and a check that each provider's target bytes are returned with their
exact commit. Git starts within those same test cases fell from
1,844 to 1,744.

The capped prebuilt Debug routine run passed the same 762 exact cases
in 224.35 s, with zero failures or skips. The Trace2 count fell from
10,092 to 9,904, below the 10,000-start target. All 188 fewer starts
were `rev-parse` queries for `HEAD^{commit}`. The run stayed below
five minutes and took 16.55 s longer than the previous run. Evidence
is in `artifacts/five-minute-pinned-routine.dfMrIu`.

### Consolidate initialization fixtures

The 762-case baseline had 302 Git-using cases. Those cases averaged
32.8 Git starts each. The other 460 cases recorded zero. The three
provider initialization classes accounted for 3,485 starts and
78.68 s of cumulative test time.

- Keep real fresh initialization for both a missing target and an
  existing empty target. The existing-target fixture also verifies
  exact CRLF and whitespace preservation and managed config capture.
- Check baseline preservation and refusal to enroll missing personality
  in one unsupported-block fixture. Keep clean and failed-commit
  recovery scenarios for both initialization history formats.
- Seed the recognized two-commit Codex history for ChatGPT init tests.
  Verify provider-first option order, unchanged runtime, and preservation
  of staged, unstaged, and untracked content.
- Compare exact runtime bytes in both checkout attachment tests.
  Dedicated status cases and the transport round trips retain public
  status checks for both providers.

The revised selection passed all 96 cases. Compared with the previous
100 cases from the same classes, Git starts in test windows fell from
3,485 to 2,944. Cumulative case time fell from 78.68 s to 69.93 s.
Evidence is in `artifacts/five-minute-init-fixtures-focused.QFMyt1`.

The capped prebuilt Debug routine run (`Category!=LegacyMigration`)
passed all 758 discovered cases in 200.98 s, with zero failures or
skips. The TRX matched discovery. Compared with the previous routine
run, wall time fell by 23.37 s (10.4%) and Git starts fell from 9,904
to 9,364. The consolidated initialization classes accounted for 15.90 s
of the observed decrease in cumulative case time. In the new run,
298 cases started Git and 460 did not. Evidence is in
`artifacts/five-minute-init-fixtures-routine.iIu1Wc`.

## Portable integration test cleanup

[Ticket #19](https://github.com/ferrywlto/ai-env-as-code/issues/19) was accepted
by Ferry on 19 November 2026.
The first slice removes five redundant Copilot backup preview cases and keeps
the real-Git refusal for a Copilot v1 runtime without a target digest.

| Removed preview cases | Retained coverage |
| --- | --- |
| Unchanged runtime | `PortableBackupAttributionTests.UnchangedRuntimeProducesNoProposal` and `PortableCopilotBackupWriterTests.NoChangeDoesNotCreateAnEmptyCommit`, which now also checks the reviewed provider, HEAD, no proposal, and unchanged runtime bytes |
| Shared, platform, and provider edits | `PortableBackupAttributionTests.OneEditedSectionProposesItsAuthoredSource` covers all six path and byte combinations. `SharedTargetStatusTests.BackupAttributionPreviewIdentifiesOneAuthoredSection` checks that the preview leaves the checkout and runtime unchanged for each provider. |
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
| `SharedTargetStatusTests.ApplyPreviewReportsRuntimeEditsWithoutWriting` | Retired one overlapping case | `PortableApplyStatusDecisionTests.ApplyAndStatusCompareExactInstructionBytes` checks the replacement decision and exact desired bytes. `LegacyRuntimeIsDifferentAndUpgradesToThePortableBlock` covers supported older blocks, and the focused read-only apply previews cover unchanged checkout and runtime bytes. |
| `PortableEnvironmentStatusTests.InstructionDriftIsDifferent` | Retired two overlapping cases | The apply/status unit suite checks drift for both providers. `StatusTests.ComparesExactBytesWithoutNormalizingLineEndings` and `PortableCopilotStatusCommandTests.ReportsRuntimeDriftWithoutWrites` cover both CLI paths, exit codes, and file preservation. |
| `PortableEnvironmentStatusTests.UnmanagedCodexConfigValuesDoNotCountAsDrift` | Retired one overlapping case | `PortableCodexStatusCommandTests.ReportsConfigStatusWithoutWriting` exercises unmanaged model settings, exact config bytes, and unchanged HEAD through the CLI. |
| Seven `StatusTests` cases from `AcceptsAQuotedManagedKey` through `IgnoresAnUnrelatedLiteralKeyContainingABackslash` | Moved seven cases to `CodexRuntimeConfigParsingTests` with direct TOML bytes | Includes both key escape forms, multiline string and nested array handling, decoded values, ambiguous multiline values, and literal keys. `StatusTests.ReportsInSyncForEqualFiles` and `PortableCodexStatusCommandTests.InvalidConfigStopsEvenWhenRuntimeInstructionsAreMissing` retain the representative CLI success and refusal. |

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
