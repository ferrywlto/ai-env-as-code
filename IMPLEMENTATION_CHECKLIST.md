# Implementation Checklist

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
| Current slice | Retained `2.0.0-alpha.4` per Ferry's decision. Release build passed without warnings and `aec version` reports the alpha value. Official release awaits Ferry's own migration verification and later request. |

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
