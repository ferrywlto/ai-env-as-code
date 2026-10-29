# AEC architecture (2.0 development branch)

This is the **current implementation**, not a target-state design. The engine,
the selected `--repo` data repository, and each local harness runtime are
separate. Git commits in the data repository are the source of truth. The
class diagrams show the main relationships, not every implementation helper.
See the [glossary](reference.md#glossary) for capture, preflight, target, preview
and fixture, including the distinction between rendered targets and target versions.

## Components

```mermaid
flowchart LR
    User[User] --> Skill[Installed AEC skill]
    User --> CLI[AEC CLI]
    User --> Split[Review existing 1.x instruction split]
    Skill --> CLI
    Skill -.->|remote preflight and approved push| Remote[(Git remote)]

    subgraph Engine[AEC engine repository and installed binary]
        CLI --> Route[AecApplication]
        Route --> Compose[Enrollment and render]
        Route --> Guard[Implicit-home binding check]
        Guard --> Attach[Codex or Copilot init attachment]
        Route --> Inspect[Status and previews]
        Route --> Writers[Backup and apply writers]
    end

    subgraph Data[Selected Git data repository: --repo]
        Sources[Shared + platform + provider authored sources]
        Targets[Enrolled platform/provider targets]
        Config[Codex canonical config.toml]
        Legacy[Legacy provider files and manual ChatGPT backups]
        History[(Git commits)]
    end

    Compose -->|render; no commit| Targets
    Legacy -->|read existing instructions| Split
    Split -->|reviewed source commit| Sources
    Legacy --> History
    Sources --> Compose
    Sources --> History
    Targets --> History
    Config --> History
    History --> Inspect
    History -->|validate legacy source and initialization history| Attach
    Targets -->|enrolled Codex or Copilot attachment| Attach
    Attach -->|machine-local path binding; no path-only commit| Runtime[Local Codex or Copilot runtime]
    Runtime -->|read other implicit harness binding| Guard
    Runtime -->|validate selected local binding for enrollment| Compose
    Inspect --> Writers
    Writers -->|backup: changed source| Sources
    Writers -->|backup: affected targets| Targets
    Writers -->|backup: managed config| Config
    Writers -->|commit exact paths| History
    Writers -->|apply: committed target/config| Runtime
    Runtime --> Inspect
```

`aec render` composes only already-enrolled targets for the current platform;
`aec init --enroll-shared` enrolls one selected provider. Neither commits nor
deploys. `status` reads, `backup` moves runtime changes into the repository and
commits, and `apply` moves committed repository content into runtime. The CLI
does not fetch or push; the installed skill coordinates remote checks and any
approved Git operations. Codex also manages root `personality` in
`config.toml`; Copilot currently manages only its instruction file.
Confirmed Codex or Copilot attachment of a clone at a new local path changes
only its runtime binding; it does not commit a machine-specific path to shared
history. Copilot requires a clean committed canonical source for this rebind.
Enrollment then trusts that local binding, while still validating committed
canonical content and history; the canonical block may name the original
machine's checkout path. Ordinary Codex and Copilot init validate a committed,
current-platform target when enrolled and compose a local portable control
block from it. Historical canonical files stay unchanged. Once a local runtime
has a portable block bound to this checkout, repeating init without the flag
does not rewrite instructions; Codex also preserves config. Status/apply own
later drift.
Ordinary init checks the other harness's implicit home before writes and stops
on a different repository binding. Explicit custom homes are outside that
cross-harness check in this 2.0 alpha slice.
The [existing-user migration](reference.md#existing-manual-migration--implemented-and-rehearsed)
uses a reviewed split followed by per-provider enrollment, target commits, and
approved apply. Classification is a user/skill review step; the engine does not
guess it or delete the historical provider files and manual backups.
The [planned interactive migration](reference.md#planned-interactive-command--not-implemented)
adds an explicitly scoped fast-forward pull and legacy-compatible capture before
scaffolding, then waits for `DONE` before source commits and enrollment/render/apply.
It is not implemented or represented as a current component above; ordinary CLI
commands retain their no-network boundaries. An incomplete invocation restarts
from preflight, with an explicit Preserve/Overwrite choice for dirty section files.

## Internal migration preflight — no CLI dispatch yet

```mermaid
flowchart LR
    Input["Repository + target format"] --> Preflight[MigrationPreflight.Read]
    Preflight --> History["InitCommand: contained Git metadata<br/>and original initialization history"]
    Preflight --> Marker["ApplyCommand: committed .aec-version<br/>with exact working/index provenance"]
    History --> Legacy["No marker: validate current legacy files<br/>then recognize format 1.0"]
    Marker --> Versions["Validate forward known path<br/>and binary major/minor capability"]
    Legacy --> Versions
    Versions --> Snapshot["Return pinned commit + formats<br/>No writes or network"]
    Snapshot --> Selection[MigrationHarnessSelection.Read]
    Selection --> Homes["AecApplication implicit home resolvers<br/>CODEX_HOME / COPILOT_HOME or profile defaults"]
    Homes --> Runtime{"Local instruction file exists?"}
    Runtime -->|No| Skip["Skip provider; no repository-folder inference"]
    Runtime -->|Yes| Bindings["Codex/Copilot full-block parsers<br/>Require selected checkout and current platform"]
    Bindings --> Selected["MigrationHarness: provider, home, runtime path<br/>Recheck pinned HEAD; no writes"]
```

This helper reuses the original initialization proof without init's clean-tree
or `main` requirement. Draft sections do not imply completion. Dirty-section and
remote preflights below are separate internal stages, not a public orchestrator.
The selector validates local runtime enrollment, not harness installation:
an existing unbound/malformed file or conflicting checkout/platform stops selection.
Recognized portable blocks remain selected for incomplete-migration retries; they
do not prove repository completion or legacy-capture readiness. All-absent homes/files
return an empty selection, never implicit enrollment from repository data.
The Git-tracked format marker denotes completed data migration, not the CLI or
managed-block version. There is no marker writer or public `migrate` route yet.

### Internal dirty-section decision

```mermaid
flowchart LR
    Selected["Pinned preflight + selected harnesses"] --> Check[MigrationDirtySections.Read]
    Check -->|"Unrelated or unsafe changes"| Stop[Stop]
    Check -->|"Clean"| Unchanged[Unchanged; no prompt]
    Check -->|"Only eligible section files"| Prompt["Prompt: TextReader / TextWriter"]
    Prompt -->|"Exactly Preserve or Overwrite"| Choice[Return decision only]
    Prompt -->|"EOF or cancellation"| Cancel[Cancelled]
    Prompt -->|"Other input"| Prompt
```

Eligible paths are the shared instructions, current-platform policy, and selected
provider overlays—not whole directories. Git status uses NUL records and disables
optional index writes; committed/indexed kinds, links and ignored sections are
checked separately. Renames, copies, conflicts, type changes, and other dirty paths
stop before the prompt. No choice writes or discards content. Later preparation
must revalidate HEAD and paths before acting; this is not a saved authorization.

### Internal remote preflight

```mermaid
flowchart LR
    Pinned["Pinned local commit"] --> Local["MigrationRemotePreflight.Read<br/>Single configured upstream + remote URL"]
    Local --> Query["GitProcess.ReadRemoteHead<br/>Bounded ls-remote --symref"]
    Query --> Match{"Checkout = upstream<br/>= advertised default branch?"}
    Match -->|No| Stop[Stop; never switch branches]
    Match -->|Yes| Plan["Recheck local HEAD/branch<br/>Return remote + URL + branch ref + advertised tip"]
```

Missing/ambiguous tracking, local-only remotes, unresolved remote HEAD, detached or
non-default checkouts, transport failures and timeouts stop. The query disables Git
terminal prompting and closes child stdin; its 30-second budget includes startup,
with up to one second to confirm Git exit after timeout termination. Transport
stderr is not echoed because it may contain credentials. No fetch, pull, checkout
or ref update occurs. An advertised tip alone proves no ancestry; future pull work
must revalidate before acting. Tests use local bare remotes, not hosted SSH/HTTPS
or real-harness integration.

### Internal fetch preparation

```mermaid
flowchart LR
    Approved[Approved remote snapshot] --> Before[Revalidate snapshot]
    Before --> Fetch["Bounded pinned-tip fetch<br/>Objects only; no destination ref"]
    Fetch --> After[Revalidate snapshot]
    After --> Compare[Compare actual commit ancestry]
    Compare --> Result["Equal / Behind / Ahead<br/>Recheck local HEAD and branch"]
    Compare -->|Diverged| Stop[Stop; never merge]
```

`MigrationFetchPreparation` compares remote name, configured URL, default branch
and advertised tip before and after fetching. The URL stays in memory and is not
echoed in errors. Fetch has a two-minute budget plus up to one second for timeout
cleanup. An empty refmap and explicit no-tags/no-submodules/no-FETCH_HEAD options
prevent configured fetch mappings from changing refs or tracking metadata.
Ancestry checks ignore replacement refs and use the fetched commit, not stale
tracking refs. Received objects may remain after a later stop; HEAD, refs, index,
authored files and runtime must remain unchanged. The result is advisory: future
fast-forward execution must revalidate again. No public migration route, pull,
merge, push or worktree advancement is added. Disposable local-remotes tests do
not prove SSH/HTTPS or real-harness integration.

### Internal clean-checkout advancement

```mermaid
flowchart TD
    Snapshot["Approved preflight + fetched tip + local enrollment"] --> Fresh[Revalidate configuration, format, enrollment, remote and ancestry]
    Fresh --> Position{Actual ancestry}
    Position -->|Equal or ahead| Keep[Keep local HEAD and drafts]
    Position -->|Behind| Clean{Clean checkout?}
    Clean -->|No| Stop[Stop; no stash or discard]
    Clean -->|Yes| Advance["Recheck remote; fast-forward only<br/>to the pinned commit"]
    Advance --> Validate[Verify exact HEAD, branch, format and enrollment]
    Keep --> Validate
    Validate -->|Valid| Ready[Return refreshed preflight]
    Validate -->|Invalid| Retain["Stop; retain retrieved history<br/>No rollback, capture or apply"]
```

`MigrationFastForward.Execute` is internal, not a public `migrate` command.
It reuses the fetch helper's replacement-ref-free ancestry check, but performs
no fetch itself. Only a behind, clean checkout may move: no merge commit, branch
switch, reset, autostash, push or runtime write. Dirty-section integration remains
a later slice. Ignored-file collisions abort rather than being overwritten.
Per-command Git settings disable hooks, recursive submodule updates, automatic
line-ending conversion and branch merge defaults without modifying user config.
Git documents [per-command hook disabling](https://git-scm.com/docs/git-config#Documentation/git-config.txt-corehooksPath)
and [ignored-file collision refusal](https://git-scm.com/docs/git-merge#Documentation/git-merge.txt---no-overwrite-ignore).
Configured external clean/smudge/process filters and enabled/custom fsmonitor
stop before validation or advancement; this conservative boundary avoids invoking
checkout tools automatically. It is not a sandbox for arbitrary Git transports or
concurrent hostile changes. After advancement, failure retains the actual checkout
and reports that inspection is needed; no destructive rollback is attempted.
Native platform and real-harness verification remain separate from local fixtures.

### Internal preparation composition

```mermaid
flowchart TD
    Start[MigrationPreparation.Run] --> Local[Validate config, format and local enrollment]
    Local --> Dirty[Inspect exact dirty section paths]
    Dirty --> Choice["Clean: no prompt<br/>Dirty: Preserve / Overwrite"]
    Choice -->|EOF or cancellation| Cancel["Return no preparation<br/>No remote access yet"]
    Choice --> Review[Revalidate HEAD, enrollment, paths, bytes and staged entries]
    Review --> Remote[Remote preflight then object-only fetch]
    Remote -->|Cancellation| Objects["Stop before advancement<br/>Received objects may remain"]
    Remote --> Advance[Existing clean-checkout advancement]
    Advance --> Context["Return refreshed preflight, harnesses,<br/>dirty paths and advisory choice"]
```

The coordinator reuses the existing helpers; it does not route a public command.
Pre-requested cancellation skips all inspection. EOF/cancellation at the dirty
prompt stops before querying a remote; cancellation is checked between bounded
remote operations and immediately before fast-forwarding. It does not interrupt
an already running Git operation. Once HEAD has advanced, cancellation cannot
return a misleading empty result: completed preparation or retained-history
validation failure reports the actual outcome. Behind plus dirty still stops,
even with Overwrite; equal/ahead retain drafts. Prompt-time byte or staging changes
invalidate the choice before remote access. Returned choices authorize no writes;
later capture/scaffolding must freshly validate their inputs. No backup, section
generation, source/target commit, render, apply, version marker or push is added.

### Internal legacy-capture preview

```mermaid
flowchart TD
    Context["Prepared checkout + original pre-fetch commit"] --> Validate["Revalidate current context<br/>Prove original commit ancestry"]
    Validate --> Read["Read bounded original/current Git files<br/>and selected legacy runtime inputs"]
    Read --> Changed{Canonical changed since original?}
    Changed -->|No| Preview[Preview runtime capture if different]
    Changed -->|Yes| Matches{Runtime matches retrieved canonical?}
    Matches -->|Yes| Noop[No capture needed]
    Matches -->|No| Stop[Stop for reconciliation; never merge]
    Preview --> Result[Return only after all selected resources validate]
    Noop --> Result
```

Preparation retains `OriginalCommit` separately from its refreshed HEAD.
`MigrationLegacyCapturePreview.Read` validates current format/enrollment and
dirty-path scope, then checks original-to-current ancestry without replacement
refs. Historical files require one exact regular-blob tree entry and a size check
before raw reading. Current files also require clean index/worktree provenance
and exact equality with the real Git blob. Instruction decisions compare bytes;
Codex config compares only the managed personality enum and preserves retrieved
canonical formatting with the existing config updater. Unrelated runtime TOML
settings never enter proposed canonical bytes. Missing/invalid personality stops,
without adding defaults. Only selected legacy providers are read; portable runtime
blocks use the separate portable preview/handoff below, never legacy whole-file copying.
Canonical legacy blocks may name another machine's checkout; local runtime must
bind to this checkout. Results are advisory byte proposals, not write authority.
No remote query, write, staging, commit, scaffolding, render or apply occurs.

### Internal legacy-capture writer

```mermaid
flowchart TD
    Reviewed[Reviewed legacy preview] --> Fresh[Recreate preview; compare branch and every input/proposal]
    Fresh -->|Stale| Stop[Stop before replacement]
    Fresh -->|No changes| Noop[Return no changed paths]
    Fresh -->|Changed files| Guard[Verify HEAD, branch, index, marker, enrollment and bytes]
    Guard --> Atomic[AtomicFile.ReplaceIfUnchanged for one canonical file]
    Atomic --> Verify[Record completed path; recheck inputs]
    Verify -->|More files| Guard
    Verify -->|Finished| Paths[Return changed paths; no staging or commit]
    Guard -->|Failure| Partial[Report completed paths; stop without rollback]
    Atomic -->|Failure| Attempt[Also report attempted path as possibly changed]
```

`MigrationLegacyCaptureWriter.Write` uses freshly derived proposals, not mutable
buffers supplied by the reviewer. The preview now pins its symbolic branch, so
a same-HEAD branch switch still invalidates review. The writer pins the raw index
and verifies that the legacy format marker remains absent around every replacement.
Already-written canonical files must match their proposed bytes; remaining files
must match their original bytes, and all selected runtime bytes stay unchanged.
Existing guarded atomic replacement preserves file mode and checks the destination.
Atomicity is per file, not across the batch. `MigrationLegacyCaptureWriteException`
reports completed paths plus any attempted path that may have changed before an
error was detected. Earlier replacements remain inspectable; there is no rollback.
Authored drafts, runtime, unrelated config, unselected providers, HEAD and index
are not written. No commit, stage, remote operation, scaffolding or public command
is added; committing these working-file changes is a later boundary.

The deterministic failure fixture uses an unwritable destination directory on
Unix (normal non-root execution) or a non-delete-sharing file handle on Windows.
Unix root may bypass that permission restriction; native platform proof remains
separate from the current local managed-code run.

### Internal legacy-capture write and commit

```mermaid
flowchart TD
    Committer[MigrationLegacyCaptureCommitter.WriteAndCommit] --> Index[Require empty staged diff]
    Index --> Preview[MigrationLegacyCapturePreview.Read: retain fresh private buffers]
    Preview --> Writer[MigrationLegacyCaptureWriter.Write: revalidate and replace]
    Writer --> Verify[Verify exact completed paths and all inputs]
    Verify -->|No changed paths| Noop[Return null; no commit]
    Verify -->|Changed paths| Stage[Stage only changed canonical paths]
    Stage --> Objects[Compare staged object bytes to proposals; verify exact paths and modes]
    Objects --> Pin[BackupCommand.CommitStagedIndex: pin tree; verify modes and blobs]
    Pin --> Publish[Compare-and-swap reviewed branch and parent]
    Publish --> Result[Verify commit and current inputs; return commit ID]
    Writer -->|Failure| Stop[Report retained files, staging or commit; no rollback]
    Stage -->|Failure| Stop
    Pin -->|Failure| Stop
    Result -->|Verification failure| Stop
```

The coordinator owns writing and committing; it does not accept already-written
paths from callers. Private fresh buffers bind staged Git object bytes directly
to the proposal. The shared commit helper optionally verifies expected modes in
its pinned tree before publishing; existing callers retain their prior contract.
HEAD, symbolic branch, enrollment, absent format marker and every selected
canonical/runtime file are rechecked around staging and after commit. A preexisting
staged diff stops before writing; unstaged authored drafts stay out of the commit.
No-op still validates the reviewed snapshot. This is not a transaction or lock:
later failure may retain working changes, staging or a new commit for inspection.
No rollback, push, scaffolding, render/apply, public route or runtime write is added.

### Internal portable-capture preview for interrupted migration

```mermaid
flowchart TD
    Input[MigrationPortableCapturePreview.Read] --> Context[Validate prepared HEAD, original ancestry and local enrollment]
    Context --> Kind{Selected runtime format}
    Kind -->|Legacy| Skip[No portable proposal; retain legacy capture path]
    Kind -->|Portable Codex| Codex[PortableCodexBackupPreview: instructions and managed personality]
    Kind -->|Portable Copilot| Copilot[PortableCopilotBackupPreview: instructions only]
    Codex --> Guards[Require clean committed sources/target and matching last-applied digest]
    Copilot --> Guards
    Guards --> Conflict{Ambiguous attribution, pulled personality conflict or differing same-source proposals?}
    Conflict -->|Yes| Stop[Stop; no merge, overwrite or writes]
    Conflict -->|No| Verify[Recheck context, runtime/config bytes and committed targets]
    Skip --> Verify
    Verify --> Result[Return nullable provider previews; no writes or commit]
```

The completion marker may still be absent after a prior migration applied one
provider. Section-aware backup previews handle that portable runtime; generated
instructions are never proposed as a legacy whole-file capture. A null provider
preview means absent or legacy; an unchanged portable provider retains its preview.
Historical regular-blob reads ignore replacement refs and reuse the legacy
preview's bounded reader and original-ancestry check. All selected runtime bytes,
including skipped legacy providers, are checked again before return.
Dirty relevant authored drafts stop even after `Preserve`: that choice does not
waive existing committed-target validation. A newly retrieved canonical personality
must agree with runtime before capture; conflicting values stop for reconciliation.
Matching proposals to the same shared/platform source are allowed; different
proposals stop without choosing a winner. Missing/stale targets, digest mismatch,
control-block edits and ambiguous sections retain existing backup refusal rules.
This is read-only preparation, not the portable writer, scaffolding, deployment
or public migration command. Later writers must freshly validate these proposals.

### Internal single-provider portable-capture handoff

```mermaid
flowchart TD
    Reviewed[MigrationPortableCaptureWriter.WriteAndCommit] --> Fresh[Require empty staged diff; recreate migration preview]
    Fresh --> Match[Compare branch, provider presence and underlying snapshot/config inputs]
    Match --> Count{Changed portable providers}
    Count -->|Zero| Noop[Verify state; return null without commit]
    Count -->|Both| Stop[Stop before writes; combined capture is deferred]
    Count -->|One| Writer[Existing PortableCodexBackupWriter or PortableCopilotBackupWriter]
    Writer --> Scope[Existing source/target writers and PortableBackupCommitter own exact commit scope]
    Scope --> Verify[Verify returned HEAD, one reviewed parent, branch, marker, enrollment and runtime bytes]
    Verify --> Result[Return commit; no runtime apply]
    Writer -->|Failure| Inspect[Report retained files, index or commit; no rollback]
    Verify -->|Failure| Inspect
```

This adapter dispatches fresh derived proposals rather than caller-supplied
proposal fields. Existing backup code owns affected enrolled targets across all
platforms/providers; the adapter does not duplicate its rendering/path rules.
All selected runtime instruction files and Codex config—including skipped legacy
state—are checked before and after handoff. Preexisting staging is never unstaged.
No-op is validated; two changed providers stop even for identical shared proposals.
The first provider's commit would otherwise invalidate the second preview's target
baseline, so combined capture needs separate work, not a sequential fallback.
After a successful backup, runtime digests deliberately remain unchanged until an
approved apply. The adapter therefore verifies raw runtime bytes rather than
recreating a migration preview after commit. A concurrent edit can be detected,
not prevented atomically; failures may leave files, staging or a commit to inspect.
No runtime write, push, scaffolding, public route or automatic rollback is added.

### Internal combined portable authored-file writer

```mermaid
flowchart TD
    Reviewed[MigrationPortableSourceWriter.Write] --> Recheck[Shared RecheckPreview: fresh inputs and empty staged diff]
    Recheck --> Plan[MigrationPortableCapturePlan: freeze baseline and deduplicate authored/config proposals]
    Plan -->|Conflict| Stop[Stop before replacing files]
    Plan --> Verify[Check HEAD, branch, raw index, absent marker, enrollment and frozen file bytes]
    Verify -->|Next changed authored/config path| Atomic[AtomicFile.ReplaceIfUnchanged]
    Atomic --> Record[Record completed path]
    Record --> Verify
    Verify -->|Finished or no-op| Paths[Return changed authored paths; no targets or Git changes]
    Verify -->|Failure| Partial[Report completed and possibly changed attempted paths; no rollback]
    Atomic -->|Failure| Partial
```

Identical shared/platform edits from both providers become one replacement;
separate overlays and managed personality can change in the same working-file batch.
Fresh proposals and fixed section paths supply destinations, not caller-held fields.
Unchanged relevant sources/targets and all selected runtime instruction/config bytes
are frozen too. A completed file must match its proposal; others retain baseline
bytes. Raw index bytes must remain identical, and staging is refused throughout.
Atomicity is per file, not a batch transaction. Failure retains inspectable partial
changes; the existing non-root Unix failure-fixture constraint still applies.
Targets intentionally remain at the old committed version at this boundary.
The target-only bridge below handles regeneration, and the separate combined
coordinator handles its commit. Public activation remains unfinished.
Clean-source previews reject these intentionally
dirty authored files. The existing
single-provider handoff still refuses two changed providers. No runtime deployment,
new enrollment, scaffolding, network operation or public command is added.

### Internal combined portable target-only writer

```mermaid
flowchart TD
    Review[Reviewed portable capture] --> Prepare[MigrationPortableTargetWriter.Prepare: fresh private plan]
    Prepare --> Enrolled[Read committed enrollments; validate baselines and compose combined desired targets]
    Enrolled --> Source[Separate authored-file writer]
    Source --> Paths[Exact completed authored/config path set]
    Paths --> Verify[Verify proposal bytes, unchanged sources, targets, runtime and Git guards]
    Verify -->|Next changed enrolled target| Replace[AtomicFile.ReplaceIfUnchanged]
    Replace --> Verify
    Verify -->|Finished or no-op| Result[Return target paths; no source, index, HEAD or runtime writes]
    Verify -->|Failure| Inspect[Report completed and possibly changed attempted targets; no rollback]
    Replace -->|Failure| Inspect
```

```mermaid
classDiagram
    class MigrationPortableCapturePlan {
        ~Prepare(reviewed)
        ~WriteSources()
        ~ReadSource(relative)
        ~ObserveCommitted(relative)
        ~Verify(writtenSources, writtenTargets)
    }
    class MigrationPortableSourceWriter {
        +Write(reviewed)
    }
    class MigrationPortableTargetWriter {
        +Prepare(reviewed)
        +Write(writtenSourcePaths)
        ~VerifyRendered()
        ~VerifyForCommit(expectedHead)
    }
    MigrationPortableSourceWriter --> MigrationPortableCapturePlan : frozen source-stage guards
    MigrationPortableTargetWriter *-- MigrationPortableCapturePlan : private pre-write baseline
    MigrationPortableCapturePlan --> MigrationPortableCaptureWriter : fresh RecheckPreview
    MigrationPortableTargetWriter --> InstructionSections : baseline and combined desired rendering
    MigrationPortableTargetWriter --> AtomicFile : target-only replacements
```

The plan exists only in memory and must be prepared before authored writes;
it is not a saved continuation or a public migration command. Shared edits affect
every committed enrollment, platform edits only that platform, and provider edits
that provider across platforms. Combined edits use the union and compose all
proposals together. No target or provider enrollment is created; config-only and
no-change capture render nothing. Unsupported committed target paths, dirty or
inconsistent baselines, links and oversized rendering stop before target writes.

The shared capture plan keeps fresh proposal buffers and enrollment private,
freezes consumed sources and all committed targets, and reuses the authored
writer's HEAD/branch/raw-index/marker/enrollment/runtime guards. The completed path
list must match exactly, and every authored/config byte must match its proposal;
it is not proof by path names alone. Rechecks surround each per-file replacement.
Failures retain partial targets for inspection, without rollback or batch atomicity.
No staging, commit, runtime write, network, scaffolding or public route is added.

### Internal combined portable capture coordinator

```mermaid
flowchart TD
    Reviewed[MigrationPortableCaptureCommitter.WriteAndCommit] --> Plan[One fresh private capture plan and affected target plan]
    Plan --> Freeze[Freeze desired source/config/target bytes and baseline Git modes]
    Freeze --> Sources[Existing authored-file writing stage]
    Sources --> Targets[Existing target-only writing stage]
    Targets --> Verify[Verify exact completed union, original index and frozen inputs]
    Verify -->|No changes| Noop[Return no commit]
    Verify -->|Changed paths only| Stage[Exact-path Git add]
    Stage --> Objects[Verify exact stage-0 paths, modes and actual object bytes]
    Objects --> Commit[CommitStagedIndex: pin tree, sign and compare-and-swap branch]
    Commit --> Check[Verify single reviewed parent, exact committed paths/blobs/modes and frozen inputs]
    Check --> Result[Return one commit; runtime unchanged]
    Sources -->|Failure| Inspect[Retain files/index/possibly commit; no rollback or push]
    Targets -->|Failure| Inspect
    Verify -->|Failure| Inspect
    Stage -->|Failure| Inspect
    Objects -->|Failure| Inspect
    Commit -->|Failure| Inspect
    Check -->|Failure| Inspect
```

```mermaid
classDiagram
    class MigrationPortableCaptureCommitter {
        +WriteAndCommit(reviewed)
    }
    MigrationPortableCaptureCommitter --> MigrationPortableCapturePlan : one private baseline and source writing
    MigrationPortableCaptureCommitter --> MigrationPortableTargetWriter : same plan, target writing and revalidation
    MigrationPortableCaptureCommitter --> BackupCommand : CommitStagedIndex
    MigrationPortableCapturePlan --> MigrationPortableCaptureWriter : RecheckPreview
```

Both providers' agreeing or separate proposals share one commit, not sequential
single-provider commits that invalidate each other's baseline. Desired bytes are
copied from the private plan before writing, never hashed from mutable working
files as authority. Staged object size is checked before reading; staged and pinned
tree modes must match the original regular-file modes. Existing signing behavior
and the guarded branch update are retained. No-op still validates the full plan.

Default source/target-stage verification still requires the original raw index
and HEAD. Only the explicitly named commit boundary accepts the staged index or
new HEAD; it retains source/config/target bytes, branch, absent completion marker,
local enrollment and all selected runtime guards. The coordinator separately
verifies exact staged/committed paths and objects, and the final index must be clean.
Checks run as named, sequential guards with descriptive local values and explicit
runtime/canonical file loops. Staged entry, size parsing, expected size and content
comparison are separate checks; diagnostics identify the failing input. This
readability split does not add a validation framework or alter the boundaries above.
An error may retain authored files, targets, staging or a published local commit;
no batch atomicity, rollback, push or runtime deployment is claimed. The existing
single-provider adapter remains unchanged. This coordinator has no public route;
scaffold writing, completion orchestration and activation are separate stages.

### Internal migration scaffold preview

```mermaid
flowchart TD
    Context[Fresh post-capture preparation] --> Validate[Validate HEAD, enrollment and reviewed dirty paths]
    Validate --> Scope[Shared + current platform + confirmed local provider overlays]
    Scope --> Observe[Read committed regular blobs, index entries and working bytes]
    Observe --> Missing{Genuinely missing?}
    Missing -->|Yes, not ignored| Empty[Propose empty Markdown seed]
    Missing -->|No| Preserve[Preserve clean text; report dirty choice, staging or deletion]
    Empty --> Recheck[Recheck context, raw index, runtime and section bytes]
    Preserve --> Recheck
    Recheck --> Preview[Return inventory only; no files created]
```

```mermaid
classDiagram
    MigrationScaffoldPreview --> MigrationPreparation : fresh post-capture context
    MigrationScaffoldPreview --> MigrationHarnessSelection : confirm local scope
    MigrationScaffoldPreview --> MigrationDirtySections : validate reviewed paths
    MigrationScaffoldPreview --> MigrationScaffoldFile : required source inventory
    MigrationScaffoldFile --> MigrationScaffoldAction : proposed disposition
```

`MigrationScaffoldPreview.Read` proposes zero bytes only when a source is absent
from the prepared commit, index and working tree. Clean files remain unchanged
under every choice. Preserve retains draft bytes or deletions; staged changes
and preserved deletions block a future writer. Overwrite remains unresolved: approving empty
missing-file seeds does not authorize erasing existing text. No instruction
classification, scaffold writing, staging, commit, transport or runtime write is
performed. The returned observations are advisory, not reusable write authority;
the writer below validates fresh state. Legacy instructions remain intact.

### Internal missing-only scaffold writer

```mermaid
flowchart TD
    Review[Reviewed scaffold preview] --> Fresh[Recreate preview; compare inputs and actions]
    Fresh --> Block{Any staged or unresolved source?}
    Block -->|Yes| Stop[Stop before creating directories or files]
    Block -->|No| Freeze[Freeze raw index, legacy files and current runtime bytes]
    Freeze --> Guard[Initial full validation; also covers no-op]
    Guard -->|Missing files| Dirs[Local path guards; create missing parents and record directories]
    Guard -->|No missing files| Return
    Dirs --> GuardAgain[Revalidate before file creation]
    GuardAgain --> Create[CreateNew empty file; never overwrite]
    Create --> Check[Revalidate; admit only our completed dirty-path additions]
    Check -->|More missing files| Dirs
    Check -->|Complete| Return[Return created paths; existing content unchanged]
    Dirs -->|Failure| Retain[Report retained files, directories and attempted path; no rollback]
    GuardAgain -->|Failure| Retain
    Create -->|Failure| Retain
    Check -->|Failure| Retain
```

```mermaid
classDiagram
    MigrationScaffoldWriter --> MigrationScaffoldPreview : fresh validation and private inventory
    MigrationScaffoldWriter --> AtomicFile : WriteNew uses CreateNew
    MigrationScaffoldWriter --> MigrationScaffoldWriteException : partial creation report
```

`MigrationScaffoldWriter.Write` ignores caller seed buffers and creates exactly
zero-byte files from its fresh fixed-path inventory. Existing clean files and
reviewed unstaged drafts are never opened for writing. Every blocker stops the
whole operation before directory creation. Full validation runs initially and
before/after each file creation, not around every directory creation or again
after the final post-file check. Directory creation retains local link/directory
guards. Rechecks allow only original reviewed
dirty paths plus files this call successfully created. Captured runtime/config
bytes protect this operation; the advisory preview did not store earlier runtime
bytes. A concurrent arrival is not overwritten. Checks are best-effort, not a
filesystem-wide transaction. Failures retain and report completed additions and
possibly changed attempted paths, including directories created before a later
pre-file validation detects drift. No staging, commit,
render, apply, transport, completion marker or public migration route is added.

## Cross-machine boundary

```mermaid
flowchart LR
    A[(A: committed portable target)] -->|approved Git push| Remote[(Git remote)]
    Remote -->|explicit exact-byte clone or fetch + fast-forward| B[(B: same committed target)]
    A -->|apply with A binding| RuntimeA[A runtime: A checkout path]
    B -->|confirmed init; apply with B binding| RuntimeB[B runtime: B checkout path]
```

Git transfers portable source and target history. Init/apply generate each
runtime's local repository binding; they do not rewrite a shared path in Git.
Checkout must preserve committed bytes: clone with `--config core.autocrlf=false`
before checkout, without content-changing filters. Clean Git diff alone is not
byte-provenance proof; see the [clone instructions](reference.md#sharing-migrated-instructions-between-machines).
Status compares runtime with the local commit, so remote freshness requires the
separate tip check and approved Git transport. The shared round-trip test runs
Codex and Copilot cases against a local bare remote and two checkouts on one
platform. Cross-platform Native AOT and real-harness evidence remain separate
release work.

## Platform lifecycle verification boundary

```mermaid
flowchart LR
    Legacy[Disposable legacy init: both providers] --> Sources[Commit reviewed sources]
    Sources --> Codex[Enroll and commit Codex target]
    Codex --> Copilot[Enroll and commit Copilot target]
    Copilot --> Apply[Apply and check status per provider]
    Apply --> Backup[Edit one provider section; backup commits]
    Backup --> Drift[Runtime digest unchanged; status reports drift]
    Drift --> Refresh[Apply refreshes digest; status in sync]
    Refresh --> Render[Shared edit; render both targets only]
```

The macOS, Windows, and Linux smoke scripts keep installer/uninstaller checks in the same
disposable lifecycle. They preserve legacy files and unrelated runtime config,
check exact backup commit paths, and verify render changes neither runtime nor
Git history. Windows and Linux also check that uninstall preserves the previously applied
authored sections, all data files, and the other provider's runtime/skill.
Each manual workflow builds Native AOT before running its script. The macOS
managed-DLL fallback checks shell sequencing only; neither that nor OS-runner
smoke proves a real harness integration. The Windows 2.0 sequence passed managed
lifecycle/migration tests, Native AOT build, and isolated installer lifecycle in
[run 40201089188](https://github.com/ferrywlto/ai-env-as-code/actions/runs/40201089188)
at `8878d04`. The prior clone-attachment failure was resolved by preserving
checkout bytes in the test fixtures, not by relaxing production provenance checks.
The same Linux 2.0 two-provider sequence passed focused/migration tests, Native
AOT build, and the complete isolated installer lifecycle in
[run 40218058322](https://github.com/ferrywlto/ai-env-as-code/actions/runs/40218058322)
at `85fbd19`. This establishes Linux ARM64 runner/native behavior, not actual
Codex/Copilot harness integration.

## Classes: source composition and runtime inspection

```mermaid
classDiagram
    class AecApplication {
        +Run(args, output, error)
    }
    class SharedEnrollmentCommand {
        +Run(repository, provider, providerHome, output)
    }
    class RenderCommand {
        +Run(repository, output)
        +CurrentPlatform()
    }
    class InstructionSections {
        +Render(shared, platform, provider)
        +Parse(content, hasPlatform, hasProvider)
    }
    class PortableRuntimeSnapshot {
        +Read(repository, provider, providerHome)
        +Commit
        +Target
        +Runtime
    }
    class CommittedTargetValidator {
        +IsEnrolled(repository, commit, platform, provider)
        +Read(repository, platform, provider)
    }
    class InitCommand {
        +Run(repository, codexHome, forcePathChange)
    }
    class CopilotInitCommand {
        +Run(repository, copilotHome, forcePathChange)
    }
    class PortableEnvironmentStatus {
        +Inspect(repository, provider, providerHome)
    }
    class PortableApplyPreview {
        +Create(repository, provider, providerHome)
        +RenderDesiredRuntime(snapshot)
    }
    class PortableCodexConfigPreview {
        +Create(snapshot)
    }

    AecApplication --> SharedEnrollmentCommand : init --enroll-shared
    AecApplication --> InitCommand : ordinary Codex init
    AecApplication --> CopilotInitCommand : ordinary Copilot init
    AecApplication --> RenderCommand : render
    AecApplication --> PortableEnvironmentStatus : status
    AecApplication --> PortableApplyPreview : apply preflight
    SharedEnrollmentCommand --> InstructionSections : compose
    RenderCommand --> InstructionSections : compose
    PortableRuntimeSnapshot --> CommittedTargetValidator : read committed target
    InitCommand --> CommittedTargetValidator : enrolled attachment
    CopilotInitCommand --> CommittedTargetValidator : enrolled attachment
    PortableEnvironmentStatus --> PortableRuntimeSnapshot : inspect
    PortableEnvironmentStatus --> PortableCodexConfigPreview : Codex only
    PortableApplyPreview --> PortableRuntimeSnapshot : inspect
    PortableApplyPreview --> PortableCodexConfigPreview : Codex only
```

The snapshot binds a committed target to the current platform, selected data
repository, and local runtime control block. It is the common read boundary;
previews plan changes but do not authorize writes without revalidation.

## Classes: directional writes

```mermaid
classDiagram
    class AecApplication
    class PortableApplyPreview
    class PortableRuntimeSnapshot
    class PortableInstructionWriter {
        +Apply(reviewed)
    }
    class PortableCodexWriter {
        +Apply(reviewed)
    }
    class PortableCodexBackupPreview {
        +Create(repository, codexHome)
    }
    class PortableCopilotBackupPreview {
        +Create(repository, copilotHome)
    }
    class PortableBackupAttributionPreview {
        +Create(snapshot)
    }
    class PortableCodexBackupWriter {
        +WriteAndCommit(reviewed)
    }
    class PortableCopilotBackupWriter {
        +WriteAndCommit(reviewed)
    }
    class PortableBackupSourceWriter {
        +Write(snapshot, attribution)
    }
    class PortableBackupTargetWriter {
        +Render(snapshot, attribution)
    }
    class PortableBackupCommitter {
        +Commit(reviewed, renderedPaths)
        +Commit(snapshot, attribution, renderedPaths)
    }

    AecApplication --> PortableApplyPreview : Codex and Copilot apply
    AecApplication --> PortableCodexWriter : Codex apply
    AecApplication --> PortableInstructionWriter : Copilot apply
    PortableCodexWriter --> PortableInstructionWriter : instruction step
    AecApplication --> PortableCodexBackupPreview : Codex backup preflight
    PortableCodexBackupPreview --> PortableBackupAttributionPreview : attribute one section
    PortableCopilotBackupPreview --> PortableRuntimeSnapshot : read committed target and binding
    PortableCopilotBackupPreview --> PortableBackupAttributionPreview : attribute one section
    AecApplication --> PortableCodexBackupWriter : Codex backup
    PortableCodexBackupWriter --> PortableBackupSourceWriter : changed authored source
    PortableCodexBackupWriter --> PortableBackupTargetWriter : affected enrolled targets
    PortableCodexBackupWriter --> PortableBackupCommitter : exact-path commit
    AecApplication --> PortableCopilotBackupPreview : Copilot backup preflight
    AecApplication --> PortableCopilotBackupWriter : Copilot backup
    PortableCopilotBackupWriter --> PortableCopilotBackupPreview : recheck inputs
    PortableCopilotBackupWriter --> PortableBackupSourceWriter : one authored source
    PortableCopilotBackupWriter --> PortableBackupTargetWriter : affected enrolled targets
    PortableCopilotBackupWriter --> PortableBackupCommitter : exact-path commit
```

The Codex backup writer rechecks runtime inputs and Git state before replacing
canonical files, then commits only the affected paths. An interrupted
multi-file write can leave partial uncommitted changes, so it reports the need
to inspect rather than rolling back automatically. Copilot `status` and
`apply` use the portable target. Public Copilot `backup` now uses the same
snapshot and one-section attribution rules. Its writer rechecks the preview,
refuses pre-existing staged changes, and commits one attributed source plus
affected targets. The retained 1.x Copilot backup helper is not routed from
the public command.
