# Implementation Checklist

## 2.0 development — shared instructions first

- [x] Make the README diagram-first and move detailed command, release, build,
  and troubleshooting material to [the reference](docs/reference.md).
- [x] Make source ownership, lazy enrollment, alignment, and planned backup
  attribution visual in the [design](docs/shared-instructions-design.md).
- [x] Distinguish local xUnit, OS-runner Native AOT smoke, and real-harness
  verification without treating test configuration as a remote pass.
- [x] Create `development/2.0` from the completed platform lifecycle work.
- [x] Record the approved policy split and lazy enrollment in
  [the shared-instruction design](docs/shared-instructions-design.md).
- [x] Implement deterministic in-memory composition using explicitly selected inputs.
- [x] Verify focused composition and version tests: 23 passed; product build
  succeeded with zero warnings or errors.
- [x] Review the [composed Codex/Copilot examples](docs/shared-instructions-preview.md)
  with the user; the shared approval and access policy and distinct provider
  overlays were approved.
- [x] Document `aec render`, portable per-platform/provider target paths, local
  AEC block generation, and single-section backup attribution.
- [x] Implement AEC-owned section boundaries and safe extraction.
- [x] Implement repository-only `aec render` for explicitly enrolled current-platform
  target directories; do not create unused targets or touch runtime/Git history.
- [x] Run focused section, render, and version tests (10 passed).
- [x] Update both bundled skills to classify approved shared, platform, and
  provider changes and route enrolled targets through render; explicitly warn
  that legacy apply does not deploy the rendered targets yet.
- [x] Validate focused upgrade and Copilot init tests (41 passed), product build
  (zero warnings/errors), whitespace checks, and both skill YAML frontmatter
  blocks. The skill-creator validator could not run because PyYAML is absent
  from the installed Python runtimes.
- [x] Complete committed-target backup migration for Copilot. Both providers'
  public `status`, `apply`, and `backup` now use committed targets.
- [x] Enroll only the explicitly used platform and harness; preserve existing
  enrolled data and manual ChatGPT files.
- [x] Add focused composition, section, and `render` xUnit tests to each
  manually dispatched macOS ARM64, Windows x64, and Linux ARM64 workflow;
  local Release scope passed 31/31, all three YAML files parse, and the
  workflow change was committed as `7d551de` without a push or remote run.
- [x] Add opt-in `init --enroll-shared` for a completed repository and one
  selected current-platform Codex or Copilot target. Require the three authored
  sources and provider baseline to match committed Git; never infer their split,
  change runtime, commit, or push.
- [x] Teach the shared/platform/provider choice through a README diagram,
  `aec help`, bundled skill guidance, and a missing-source error; focused
  Release coverage passed 83/83 including enrollment, rendering, skill upgrades,
  and provider init. The exact help-contract test passed separately.
- [x] Preserve the legacy Codex init lifecycle (82/82 affected init tests passed)
  and validate both skill frontmatter blocks and all three workflow YAML files.
  The skill-creator validator cannot start without PyYAML; frontmatter was
  parsed independently with Ruby.
- [x] Dispatch all three workflows against `4223f0e`: [macOS ARM64](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36786570129)
  and [Linux ARM64](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36786570825)
  passed focused tests, Native AOT build, and isolated smoke checks.
- [x] Resolve the Windows test-fixture cleanup failure from
  [the first run](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36786568936).
  The [rerun at `3424fe1`](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36789513384)
  passed 41/41 focused tests, Native AOT build, command dispatch, and isolated
  installer/uninstaller checks. This is OS-runner evidence, not real-harness
  verification.
- [x] Add Git-host-agnostic remote/default-branch preflight to both bundled
  skills without adding a CLI `sync` command or implicit push. Keep no-remote
  use local-only with a warning, and pause on ambiguous or divergent Git state.
- [x] Narrow both bundled skills to AEC-action preflight: local drift and a
  read-only remote-tip check happen before a canonical write or runtime apply,
  never on unrelated replies. Equal commits need no fetch; differing commits
  require an approved fetch before classifying ahead/behind/diverged. Do not
  run an automatic `git pull` or add a conversation-start check. Both skill
  frontmatter blocks parsed and `git diff --check` passed; the skill-creator
  validator could not start because PyYAML is unavailable.
- [ ] In a separate packaging slice, bump the prerelease version and recognize
  the exact previous bundled skill bytes so installed-skill upgrade can deploy
  this revised guidance; do not change the installed personal skill here.
- [x] Package the skill guidance as 2.0.0-alpha.4, recognize exact alpha.3
  predecessor hashes, and pass 42/42 focused Release tests. Both skill YAML
  frontmatter blocks parse; the skill-creator validator is unavailable because
  its Python runtime lacks PyYAML. The installed personal skill was not changed.
- [x] Run the new macOS ARM64 Native AOT `aec render` smoke against a disposable,
  enrolled Codex target. The test checks exact rendered bytes, idempotence,
  unchanged Git HEAD/index, untouched Codex/Copilot runtime files, and no unused
  provider target. Shell syntax checks and 14/14 focused managed tests passed.
  The [macOS runner](https://github.com/ferrywlto/ai-env-as-code/actions/runs/37075248187)
  at `4798fe4` passed the focused tests, Native AOT build, command dispatch,
  and complete isolated lifecycle. The local Command Line Tools linker remains
  incompatible with current SDK architecture entries; runner success does not
  validate a real harness or repair the local toolchain.
- [x] Run the new Windows x64 Native AOT `aec render` smoke in the existing
  disposable installer lifecycle. It checks exact target bytes, idempotence,
  unchanged Git HEAD/index, untouched Codex/Copilot runtime files, and no
  unused provider target. The [Windows runner](https://github.com/ferrywlto/ai-env-as-code/actions/runs/37120719391)
  at `7a8987f` passed focused tests, Native AOT build, command dispatch, and
  the complete isolated lifecycle. PowerShell is unavailable on the local
  macOS host; runner success is not real-harness verification.
- [x] Run the new Linux ARM64 Native AOT `aec render` smoke in the existing
  disposable installer lifecycle. It checks exact target bytes, idempotence,
  unchanged Git HEAD/index, untouched Codex/Copilot runtime files, and no
  unused provider target. Local shell syntax and 14/14 focused managed tests
  passed. The [Linux runner](https://github.com/ferrywlto/ai-env-as-code/actions/runs/37129095970)
  at `4706008` passed focused tests, Native AOT build, command dispatch, and
  the complete isolated lifecycle. Runner success is not real-harness verification.
- [x] Add a repository-only committed-target validator for an explicitly selected
  platform/provider. It requires clean, committed authored inputs and target
  bytes that exactly match composition; focused tests cover Codex/Copilot clean
  targets and missing, untracked, stale, staged, and unstaged cases. The focused
  Release scope passed 22/22 and the product build passed with zero warnings.
  It does not change `status`, `backup`, `apply`, runtime, or local harness
  activation.
- [x] Add an internal read-only 2.0 instruction status helper for one explicitly
  selected local provider home. It validates the committed target, requires an
  existing runtime file to bind to the selected data repository, and compares
  the complete generated-block-plus-target bytes. Missing runtime reports
  `missing`; absent home, wrong binding, stale target, or uncommitted target
  stops. The focused new and legacy status scope passed 74/74 Release tests,
  including Codex's ChatGPT-aware managed block; the product build passed
  with zero warnings. Public commands and runtime remain unchanged.
- [x] Add in-memory 2.0 generated local control blocks: Codex versions 7/8
  retain the ordinary/ChatGPT-aware distinction, and Copilot version 2 names
  its selected platform target. Strict binding recognition rejects altered
  target paths and unsupported platforms; the internal status helper compares
  these versions while public 1.x generators remain unchanged. Focused block
  and status tests passed 72/72; adjacent init, enrollment, and uninstall tests
  passed 40/40, plus 6/6 focused Codex init compatibility cases. The product
  build passed with zero warnings. No runtime file or public command was changed.
- [x] Add an internal 2.0 apply preview with no write path. A shared runtime
  snapshot validates the committed target, explicit provider home, repository
  binding, and v2 platform binding; missing or mismatched runtime stops with
  init guidance. The preview computes v2 block plus target bytes, preserving
  Codex's ChatGPT-aware variant, and reports `WouldReplace`. Internal status
  now reports legacy blocks as drift against those same v2 bytes. The focused
  block, validator, and old/new status scope passed 147/147 Release tests; the
  product build passed with zero warnings. Public commands and config remain
  unchanged.
- [x] Add an internal one-file 2.0 instruction writer, disconnected from the
  public CLI. It accepts a reviewed snapshot, revalidates HEAD, committed
  target, and runtime, then uses compare-before-replace to write exact v2
  bytes or report `unchanged`. Explicit apply can replace pre-existing runtime
  drift; edits detected after preflight stop. Disposable Codex/Copilot tests
  cover idempotence, ChatGPT-aware guidance, config preservation, changed
  runtime, changed HEAD, and dirty target. Focused Release tests passed 52/52
  and the product build passed with zero warnings. The filesystem helper is
  not a cross-process atomic compare-and-swap; public CLI and real runtime
  remain unchanged.

- [x] Approve the Codex config preflight contract for 2.0 apply in
  [the shared-instruction design](docs/shared-instructions-design.md): one
  committed root `personality` value, both runtime files planned before any
  write, exact-byte freshness checks, config-first replacement, and explicit
  partial-update reporting without automatic rollback. This is a design
  decision only; no public command or runtime behavior changed.

- [x] Implement an internal, read-only Codex config preflight alongside the
  2.0 instruction preview. It requires the committed canonical root
  `personality`, plans only that runtime value, and reports missing, invalid,
  staged, and unstaged config cases without writing. The focused shared-target
  scope passed 49/49 Release tests; the product built with warnings as errors
  and zero warnings. Ferry reviewed and approved this slice. Public commands
  and personal runtime remain unchanged.
- [x] Add an internal Codex config-plus-instructions writer, disconnected from
  public `apply`. It rechecks reviewed Git and runtime bytes, replaces config
  first, rechecks both inputs, then replaces instructions; failures after the
  config step report possible partial state without automatic rollback.
  Disposable tests cover idempotence, missing config, instructions-only apply,
  pre-write drift, and config/runtime/HEAD changes between steps. The focused
  Release scope passed 59/59 and the test project built with warnings as errors
  and zero warnings. Ferry reviewed and approved this slice; personal runtime
  is untouched.
- [x] Review the internal read-only single-section backup attribution preview.
  It separates the exact local AEC control-block prefix from the committed
  rendered target, compares shared/platform/provider sections, and reports one
  changed authored source path or stops on multiple changes. Disposable
  Codex/Copilot tests passed 69/69 in the focused Release scope, and the test
  project built with warnings as errors and zero warnings. Ferry reviewed and
  approved this slice. No public command, repository source, or personal runtime
  was changed.
- [x] Review the Codex portable-block target-digest slice. Newly rendered
  standard and ChatGPT-aware Codex blocks record SHA-256 of the authored target
  body, and their parser exposes that last-applied digest. Earlier portable
  blocks remain readable. The focused managed-block and shared-target scopes
  passed 121/121, then 54/54 managed-block tests after adding compatibility
  cases; the Release test build had zero warnings. Ferry reviewed and approved
  the slice, including the explanatory odd/even version-pair comment. Public
  commands and personal runtime remain unchanged.
- [x] Review the Copilot portable-block target-digest slice. Newly rendered v3
  blocks record SHA-256 of the authored target body; v1/v2 bindings remain
  readable, and the internal renderer can upgrade v2 without changing authored
  bytes. Focused block tests passed 9/9, shared-target tests passed 69/69, and
  the Release build had zero warnings. Ferry reviewed and approved the slice.
  Public commands and personal runtime remain unchanged.
- [x] Review the named managed-block versions and read-only stale-baseline
  backup guard. Both providers now reject unstamped and older same-repository
  targets before attributing a runtime edit; existing one-section edits remain
  attributable. Focused Release tests passed 137/137, the final Copilot block
  scope passed 9/9 after a naming-only edit, and the test build had zero
  warnings. Ferry reviewed and approved this slice. Public `backup` and
  personal runtime remain unchanged.
- [x] Review the read-only backup rejection-hardening tests. Both providers
  stop on source-marker collisions, altered section boundaries, managed
  control-block edits, and text outside rendered sections, without changing
  runtime or repository state. The focused Release scope passed 82/82 and the
  test build had zero warnings. Ferry reviewed and approved this slice; no
  production code changed.
- [ ] Reword the five earlier unpushed slice commits with semantic `feat:`
  subjects. Ferry approved the correction, but the first rebase attempt stopped
  before replay because this environment denied creation of `.git/rebase-merge`;
  HEAD and the checkout were verified unchanged. Do not retry automatically.
- [x] Review the internal authored-source writer substep of ticket #2 slice 5.
  It rechecks the reviewed runtime, HEAD, target, attribution, and clean source
  before replacing exactly one authored source in the repository. It does not
  render targets, commit, touch personal runtime, or change public `backup`.
  Disposable Codex/Copilot tests passed 93/93 in the affected Release scope;
  the warning-as-error build had zero warnings. Ferry reviewed and approved
  this substep. Slice 5 stays incomplete until affected enrolled targets are
  rendered and reviewed.
- [x] Review the internal all-platform target-rendering substep of ticket #2
  slice 5. It uses committed enrollment paths, selects only targets consuming
  the one changed source, checks every target's committed baseline before any
  write, then replaces affected repository targets with freshness checks and
  explicit partial-state reporting. It does not detect installed harnesses,
  create unused targets, commit, write runtime, or change public commands.
  Disposable focused tests passed 11/11 across all three supported platform
  paths; the existing public-render tests passed 4/4 and the warning-as-error
  build had zero warnings. Ferry reviewed and approved this substep.
- [x] Review ticket #2 slice 6's internal exact-path backup commit step. It
  rechecks the rendered enrolled target set and runtime, refuses unrelated
  staged changes, stages only the reviewed source and affected targets, and
  reuses the pinned-tree Git commit guard. It is not connected to public
  `backup`, does not touch personal runtime or data, and does not push. Focused
  commit tests passed 7/7, existing renderer tests passed 11/11, and the
  warning-as-error Release build had zero warnings. Ferry reviewed and approved
  this slice.
- [x] Review ticket #2 slice 7's internal 2.0 environment status. It compares
  committed rendered instructions for both providers and, for Codex only,
  compares the managed root personality without treating unrelated config as
  drift. Missing instructions report `missing`; missing personality reports
  `different`; invalid config or binding stops. It does not change public
  `status` or write runtime. Focused tests passed 14/14, existing status/config
  preview smoke tests passed 4/4, and the warning-as-error Release build had
  zero warnings. Ferry reviewed and approved this slice.
- [x] Review ticket #2 slice 8's public Codex apply migration. Ordinary Codex
  `aec apply` now requires an enrolled committed target and uses the reviewed
  config-first portable writer; it has no 1.x source fallback. Init/attachment
  still use their checkpointed legacy writer, Copilot apply is unchanged, and
  no personal runtime or data repository was touched. Public command tests
  passed 12/12; retained legacy apply tests passed 46/46, Copilot apply tests
  12/12, guarded writer tests 10/10, focused init lifecycle tests 3/3, and
  the warning-as-error Release build had zero warnings. Ferry reviewed and
  approved this slice.

- [x] Review ticket #2 slice 9's public Copilot apply migration. Ordinary
  Copilot `aec apply` now requires an enrolled committed current-platform
  target and an existing, correctly bound runtime file; it has no 1.x source
  fallback. Provider init retains its first-installation flow. Public apply
  tests passed 14/14, adjacent init/Codex apply/shared guard tests passed
  118/118, init-argument tests passed 3/3 after removing a redundant parser
  branch, and the warning-as-error Release build had zero warnings. No
  personal runtime or data repository was touched. Ferry reviewed and
  approved this slice.

- [x] Review ticket #2 slice 10's public status migration. Codex and Copilot
  status now validate the enrolled committed current-platform target and
  local binding, with no 1.x source fallback. Codex compares only the managed
  root personality; per-file `missing` labels and 0/2/1 exit codes remain.
  Public and internal status tests passed 73/73 across focused runs, and the
  warning-as-error Release build had zero warnings. No personal runtime or
  data repository was touched. Ferry reviewed and approved this slice.

- [x] Review ticket #2 slice 11's first, read-only Codex backup checkpoint.
  It combines exact instruction attribution with a managed root-personality
  proposal against one committed HEAD. Config-only and one-section-plus-config
  drift are previewable; missing personality and ambiguous instructions stop.
  It does not write or change public `backup`. Focused tests passed 7/7,
  adjacent attribution tests passed 23/23, and the warning-as-error Release
  build had zero warnings. No personal runtime or data repository was touched.
  Ferry reviewed and approved this checkpoint.
- [x] Review ticket #2 slice 11's internal guarded Codex backup writer. It
  rechecks the combined preview and refuses a staged index before writing.
  Config-only drift commits only canonical `config.toml`; one attributed
  instruction section plus config drift commits the source, affected target,
  and config together. The writer compares reviewed HEAD and runtime inputs,
  then uses a fresh validated proposal rather than duplicating comparisons of
  derived fields. Public `backup` remains unchanged. New writer tests passed
  6/6, preview tests 7/7, existing committer tests 7/7, and the
  warning-as-error Release build had zero warnings. No personal runtime or
  data repository was touched. Ferry reviewed and approved this checkpoint.

- [x] Review ticket #2 slice 11's public Codex backup migration. It requires a
  current enrolled target and stamped runtime binding, captures config-only
  or one instruction section plus config in one exact-path commit, warns and
  stops on missing personality, and rejects a detached branch before writing.
  There is no 1.x fallback. Retained initialization backup tests run directly
  against the legacy helper; public path-safety tests now use portable fixtures.
  Public backup/committer tests passed 24/24; legacy backup tests 28/28;
  affected public backup, skill-upgrade, and Copilot backup tests 52/52; public
  Copilot status tests 9/9. The warning-as-error Release build had zero
  warnings. Skill frontmatter parsed successfully with Ruby; the skill-creator
  validator could not start because PyYAML is absent. No personal runtime or
  data repository was touched. Ferry reviewed and approved this slice.

- [x] Review the current 2.0 component and class diagrams in
  `docs/architecture.md`. They distinguish the engine, selected data repo,
  local runtime, and skill-controlled remote operations; mark Copilot backup
  as legacy; and correct four stale public-routing code comments. Ferry
  reviewed and approved this documentation slice.

- [x] Review ticket #2 slice 12's read-only Copilot backup preflight. It
  reuses the committed-target snapshot and single-section attribution, checks
  that HEAD stays stable through the preview, and never writes or changes the
  public Copilot backup command. Focused preview tests passed 6/6, and new
  preview plus existing attribution tests passed 29/29; the
  warning-as-error Release build had zero warnings. `docs/architecture.md`
  identifies this internal preview and the still-legacy public route. No
  personal runtime or data repository was touched. Ferry reviewed and
  approved this slice; its unused `HasChanges` convenience property was
  removed before commit.

- [x] Review ticket #2 slice 12's internal guarded Copilot backup writer. It
  rechecks the read-only preview, rejects a detached HEAD or staged index
  before canonical writes, and uses the shared source, affected-target, and
  exact-path commit steps. Public Copilot `backup` remains unchanged. Writer,
  preview, and shared committer tests passed 19/19; the warning-as-error
  Release build had zero warnings. `docs/architecture.md` identifies the
  internal writer and still-legacy public route. No personal runtime or data
  repository was touched. Ferry reviewed and approved this slice.

- [x] Review ticket #2 slice 12's public Copilot backup migration. It requires
  a current enrolled committed target and stamped runtime binding, attributes
  one changed instruction section, and commits only its source plus affected
  targets. There is no 1.x public fallback. The old single-file helper tests
  now invoke that helper directly; new tests cover the public portable route.
  All Copilot backup-focused tests passed 29/29; adjacent Copilot init tests
  passed 13/13; the warning-as-error Release build had zero warnings. The
  bundled Copilot skill frontmatter parsed with Ruby; the skill-creator
  validator could not start because PyYAML is absent. README, reference,
  design notes, and architecture diagrams reflect the new route. No personal
  runtime or data repository was touched. Ferry reviewed and approved this
  slice.

- [x] Review ticket #2 slice 13's Codex local-path attachment. A disposable
  two-checkout test confirms a different-path clone still requires explicit
  `--force-path-change`, then binds only the local runtime block and verifies
  portable apply/status without a path-only commit or invented Copilot home.
  Canonical bytes and Git `HEAD` stay unchanged. A moved-repository test also
  covers ChatGPT-aware block preservation and repeat attachment. The bundled
  Codex skill, reference flow, and architecture diagram reflect the runtime-
  only write. Fresh-init tests were corrected to expect portable `status` to
  stop until enrollment instead of falling back to legacy canonical content.
  Focused clone tests passed 4/4, fresh-init cases 2/2, interrupted-init
  resume cases 4/4, and adjacent attachment/skill-upgrade tests 33/33;
  Release build had zero warnings. The
  wider init/apply test run was stopped after its stale fresh-init assertion
  was identified, not counted as a full pass. No personal runtime or data
  repository was touched. Ferry reviewed and approved this slice; committed as
  `d28249f`.

- [x] Review ticket #2 slice 14's implicit-home conflict guard. Ordinary Codex
  and Copilot init now read the other harness's effective implicit home
  (`CODEX_HOME`/`COPILOT_HOME`, then the user-profile default) before mutation.
  A different AEC repository binding stops both providers, including confirmed
  Codex `--force-path-change`. Explicit custom-home flags remain available but
  are outside this cross-harness guarantee; README, reference, bundled skills,
  and architecture diagram state that 2.0 alpha limitation. Disposable tests
  used temporary environment homes rather than personal runtime, and covered
  both conflict directions plus same-path/no-other-harness behavior. Focused
  init and skill-upgrade tests passed 45/45; warning-as-error Release build
  had zero warnings; `git diff --check` passed. No personal runtime or data
  repository was touched. Ferry reviewed and approved this slice; committed as
  `17eccfe`.

- [x] Review ticket #2 slice 15's Copilot pulled-repository attachment. A clean,
  committed Copilot canonical source may retain another machine's path;
  `--force-path-change` confirms a local runtime-only rebind. Without the flag,
  init stops before writes; after a confirmed bind, rerunning init is stable.
  The canonical file and Git `HEAD` must remain unchanged, and an implicit
  Codex-home conflict still stops. Keep ChatGPT provider force mode invalid.
  Disposable clone and dirty-source cases, block tests, and adjacent skill
  upgrades passed 56/56; the warning-as-error Release build had zero warnings.
  Exact help and ChatGPT force-mode dispatch tests passed 4/4 separately.
  Both bundled skill frontmatter blocks parsed and `git diff --check` passed.
  No personal runtime or data repository was touched. Ferry reviewed and
  approved this slice; committed as `d950231`.

- [x] Review ticket #2 slice 16's Codex repeat-attachment behavior. A first
  attachment to a pulled checkout still requires `--force-path-change` when
  canonical records another machine's path. Once local runtime is bound to
  `--repo`, repeating init leaves its instructions and config unchanged, even
  after portable apply; status/apply own later drift. Both harnesses still
  stop on an implicit-home conflict even with the flag. Keep canonical bytes
  and Git `HEAD` unchanged. Disposable Copilot-first, moved-checkout, portable
  repeat, both Codex conflict variants, and skill-upgrade tests passed 34/34;
  both Copilot conflict variants passed 2/2 separately. Warning-as-error
  Release build had zero warnings, skill frontmatter parsed, and whitespace
  checks passed. No personal runtime or data repository was touched. Ferry
  reviewed and approved this slice; committed as `82c09e0`.

- [x] Review ticket #2 slice 17's pulled-checkout enrollment. After confirmed
  Codex or Copilot init binds local runtime to `--repo`, shared enrollment
  accepts clean committed legacy canonical instructions even if their AEC
  block records another machine's path. It still rejects a runtime bound to
  the wrong checkout, creates only the selected portable target, and leaves
  canonical bytes, runtime bytes, and Git `HEAD` unchanged. Focused enrollment
  tests passed 14/14; the warning-as-error Release build had zero warnings.
  No personal runtime or data repository was touched. Ferry reviewed and
  approved this slice; committed as `9cbd013`.

- [x] Review ticket #2 slice 18's enrolled Codex attachment. Ordinary init
  validates the committed current-platform target and composes a local portable
  block when the runtime is missing or still legacy. It retains the historical
  canonical file and Git HEAD, preserves a locally bound portable runtime on
  repeat init, and keeps unenrolled attachment unchanged. A different checkout
  still needs confirmation; a dirty target or implicit cross-harness conflict
  stops. Enrolled clone and adjacent attachment/conflict cases passed 5/5;
  enrollment and retained legacy cases passed 17/17; the final expanded clone
  case passed 1/1. The warning-as-error Release build had zero warnings and
  whitespace checks passed. No personal runtime or data repo mutation. Ferry
  reviewed and approved this slice; committed as `6d15df8`.

- [x] Review ticket #2 slice 19's enrolled Copilot attachment. Ordinary init
  detects a committed current-platform Copilot target, validates its authored
  sources and historical canonical block, and writes a local portable runtime
  block only after the existing path-confirmation rule is satisfied. A locally
  bound portable runtime stays unchanged on repeat init; the unenrolled path
  remains intact. Dirty target/canonical inputs and a wrong runtime platform
  stop before runtime writes. Copilot init and adjacent Codex clone tests passed
  21/21; enrollment and target validation passed 22/22. The warning-as-error
  Release build had zero warnings. Disposable tests verified unchanged
  canonical bytes and Git HEAD; no personal runtime or data repo was touched.
  Ferry reviewed and approved this slice; committed as `3e11ed1`.

- [x] Review ticket #2 milestone 13a's existing-user migration rehearsal before
  CLI syntax work. A disposable committed 1.x-format repository contains Codex,
  Copilot, a non-default personality, unrelated runtime settings, and manual
  ChatGPT backup bytes. An explicit reviewed split accounts for every authored
  instruction, enrolls and commits one target at a time, applies both providers,
  verifies in-sync status, and safely repeats enrollment/init/render/apply/backup.
  Original provider/config/backup bytes and Git ancestry are preserved. The
  reference now documents the sequence and fixes stale target-consumption and
  fresh-init guidance; no new production command is required. The focused
  Release rehearsal passed 1/1 (26 seconds of Git/filesystem integration); the
  warning-as-error product/test build had zero warnings, whitespace checks
  passed, and an independent read-only review found no actionable issue.
  Historical-binary, Native AOT, real-harness, and full regression runs were not
  part of this slice. Ferry reviewed and approved the rehearsal and runbook;
  committed as `1e967e6`.

- [x] Review ticket #2 milestone 16's first, Codex-only Git round-trip checkpoint.
  Two disposable data checkouts at different paths publish/retrieve through a
  local bare remote. B requires explicit path confirmation and its own existing
  home, then uses its local runtime binding. A reviewed shared-source revision
  is rendered, committed, and explicitly pushed; B remains locally in sync until
  explicit fetch/fast-forward, then reports drift until apply. Final target bytes
  and commit IDs agree while each runtime keeps its own checkout path; legacy
  canonical bytes, history, and unrelated config survive. No Copilot target is
  created. The focused Release rehearsal passed 1/1 (20 seconds), the product/test
  warning-as-error build had zero warnings, whitespace checks passed, and an
  independent read-only review confirmed the acceptance coverage. Ferry reviewed
  and approved this checkpoint; committed as `3d4f8e0`. This is managed-code, same-host
  Git evidence, not real machines, a hosted service, or Native AOT verification.

- [x] Review ticket #2 milestone 16's matching Copilot checkpoint. The existing
  transport rehearsal is now one `GitRoundTripTests` theory for Codex/Copilot,
  retaining provider-specific binding/config assertions. Copilot confirms B's
  new path before attachment, transfers a reviewed source update through
  explicit push/fetch/fast-forward, and verifies status/apply while keeping
  `config.json` byte-identical in both homes. No unused Codex target or B runtime
  is created. The focused Release theory passed 2/2 (Codex and Copilot, 45 seconds
  total); the product/test warning-as-error build had zero warnings, whitespace
  checks passed, and independent read-only review found no actionable issue.
  Ferry reviewed and approved the checkpoint; milestone 16 is complete.

- [x] Implement ticket #2 milestone 17's macOS portable lifecycle smoke update.
  The isolated script commits the reviewed split, enrolls/commits each provider
  separately, and exercises apply/status, provider-only backup with exact commit
  paths, post-backup digest drift/refresh, and idempotence. Shared render updates
  both enrolled targets without runtime/index/history writes. Legacy canonical
  files, unrelated Codex config, and Copilot config.json remain byte-identical;
  existing installer/uninstaller safety checks are retained. The manual macOS
  workflow includes the matching focused lifecycle tests. Architecture/reference
  explain the sequence and distinguish prior runner evidence from this update.
  Shell syntax, YAML parsing, whitespace checks, independent read-only review,
  and the complete isolated script using the existing Release managed DLL passed.
  This fallback is not Native AOT or real-harness verification.
- [x] Finish focused macOS managed verification. The existing Release assembly
  run (`--no-build --no-restore`) passed 110 tests in 2m50s before the 30-second
  watchdog aborted `LegacyMigrationTests.ReviewedLegacySplitEnrollsBothProvidersAndRerunsWithoutDataLoss`.
  Its sequence attachment names that fixture as unfinished; no assertion failed,
  but the run was aborted and is not an overall pass. The workflow now isolates
  this Git-heavy rehearsal under a bounded 2-minute watchdog, keeping 30 seconds
  for smaller tests. This gives headroom above its prior 26-second pass; it does
  not establish why that run exceeded 30 seconds. Ferry approved the retry on
  2026-10-24: both separated scopes were attempted once using the existing
  Release assembly, with distinct result directories and no build/restore.
  Both aborted before executing tests because VSTest's local communication
  socket bind was denied by the sandbox (`SocketException (13)`). No counts or
  sequence attachments were produced; this is not a test pass or assertion
  failure. Ferry approved a further retry with local socket permission:
  the separate scopes passed on 2026-10-24 using the same Release binaries,
  without build/restore. Focused lifecycle: 110/110 passed in 2m16s; legacy
  migration: 1/1 passed in 42s, within its 2-minute guard. Both TRX reports under
  `artifacts/macos-socket-retry-20261024/` record completed runs with zero failed,
  aborted, or skipped tests. This verifies the selected scopes, not full regression.
- [x] Complete local macOS Native AOT build and isolated lifecycle verification.
  The first build attempt stopped during restore with NU1900: NuGet and the
  configured GitHub Packages vulnerability feeds were unreachable. Compilation
  did not start. Ferry approved the networked retry on 2026-10-24: restore and
  native compilation succeeded without an audit bypass; the built artifact's
  `version` (`2.0.0-alpha.4`) and `help` commands passed. The complete isolated
  native installer/uninstaller, both-provider portable lifecycle, and render
  smoke passed. This is current local Native AOT evidence, not a GitHub runner,
  full regression, or real-harness pass. No installed personal binary, skill,
  runtime, or data repository was changed.
- [x] Ferry reviewed and approved the verified macOS checkpoint for commit;
  ticket #2 milestone 17 is complete. Native smoke and both focused managed
  scopes passed locally; no GitHub runner or real-harness pass is claimed.

- [x] Implement the approved Windows x64 portable smoke update for ticket #2
  milestone 18. Both providers now follow reviewed sources, separate enrollment/
  target commits, apply/status, exact-path single-section backup, digest drift/
  refresh, idempotence, and shared render. Native exit codes and exact ordered
  output are checked inside helpers; fixtures use UTF-8/no-BOM/LF and isolated
  Git signing/newline settings restored in `finally`. Existing installer safety
  checks remain, including apostrophe/custom paths and refused cleanup.
  Uninstall preserves applied authored sections, the data file tree, and the
  other provider's runtime/config/skill. The manual Windows workflow matches
  macOS's focused scopes and separate bounded migration rehearsal.
- [ ] Validate the updated Windows sequence on its Windows x64 runner before
  marking milestone 18 complete. PowerShell is not installed on the local macOS
  host; local PowerShell parsing and native execution remain unavailable. Workflow
  YAML/manual-trigger/permissions/runner checks, all 11 referenced test-class
  checks, whitespace checks, and independent read-only review passed locally. At
  that initial source-only checkpoint no dependency installation, personal
  environment change, commit, push, or workflow dispatch was performed.
  Historical runner passes cover the old
  sequence only; the reference and architecture diagram distinguish this.

- [x] Pass the approved pre-push full Debug regression gate after the repair.
  The rebuilt focused scope passed 8/8; the full Debug retry passed 639/639 in
  16m19s, with zero failures, skips, timeouts, or aborts. The completed TRX report
  is under `artifacts/regression-repair-20261024/debug/`. The conditional
  commit/push/manual Windows workflow gate may now resume. Historical first
  attempt and diagnosis follow for traceability: the completed
  2026-10-24 run executed 639 tests in 17m12s: 636 passed, 3 failed, none skipped
  or aborted, with a bounded 2-minute hang guard. The TRX report is under
  `artifacts/windows-gate-20261024/debug/`. Restore and compilation succeeded.
  Commit/push/Windows workflow dispatch were conditional on a pass and did not
  run. The three failures are identified; no automatic retry or fix was made:
  - `FifoProbeTests.ZeroLengthFifoDoesNotBlockStatus`: the legacy fixture has no
    Git initialization/commit or portable target. Current status rejects it at
    committed-HEAD preflight, before it reaches the FIFO. Its expected drift
    output no longer described the current public status contract. The original
    timeout-recovery writer could also block; the repair below removes it.
  - `PortableCodexBackupPreviewTests.MissingRuntimePersonalityStopsWithoutWriting`
    and `MissingRuntimeConfigStopsWithTheSameClearError`: both tests expected exactly
    `InvalidOperationException`, but the deliberate missing-personality warning
    path now throws its specific `MissingRuntimePersonalityException` subclass.
- [x] Implement Ferry's approved narrow test-only repair. The FIFO test keeps
  its runtime AGENTS.md path, starts from a valid committed portable layout,
  verifies a healthy baseline, and asserts the exact post-read binding error
  for the empty FIFO. A child CLI with concurrent output draining, a 10-second
  wait and bounded 5-second kill/reap replaces the unbounded writer recovery.
  The process-state collection serializes inherited environment state. Both
  backup-preview tests now assert the exact missing-personality exception,
  retaining all diagnostic and no-write checks. No production file or test
  removal was needed. Independent review found no blocking issue; the rebuilt
  focused Debug scope passed 8/8 in 8s. The full Debug gate from that same build
  passed 639/639 as recorded above. The repair was committed as `d55ba51`, separate
  from the Windows smoke update.
- [x] Commit/push the approved Windows smoke update as `91ce984`, after the full
  Debug gate passed. The branch is synchronized with origin; both semantic
  commits are published. Explicitly dispatch and verify Windows run
  [40141526871](https://github.com/ferrywlto/ai-env-as-code/actions/runs/40141526871)
  at `91ce9845e92e7258c2c8c74c6e5723e72a39f0c1` on `development/2.0`.
- [ ] Resolve the current Windows managed verification failure before proceeding.
  The run completed 110 focused tests: 108 passed, 2 failed, none skipped or
  aborted (4m20s test duration). Both failures are
  `SharedEnrollmentTests.ConfirmedCloneCanEnrollWithoutRewritingCanonicalRepositoryPath`
  (Codex/Copilot), at the attachment exit-code assertion on line 80, before
  enrollment. Legacy migration, Native AOT build, version/help, and the PowerShell
  installer lifecycle were skipped; none is verified by this run. No automatic
  rerun or source fix was made.
  The runner's effective config and `attached.Error` were not logged, so its
  exact cause still needs a new runner result; the reproduced mechanism follows.
- [x] Reproduce clone newline conversion under the approved diagnostic slice.
  Disposable local Git config with `core.autocrlf=true` produced clean Git status
  but different committed/raw file hashes. Both public provider attachment
  commands exited 1 with `bytes do not exactly match the committed Git blob`.
  New clones with `--config core.autocrlf=false` attached successfully for both
  providers under the same parent config, retaining clean repository status.
  No personal installation, runtime, or data repository was touched.
- [x] Repair both enrollment clone fixtures with conversion disabled before
  checkout, and include Git/application errors in success assertions. Document
  exact-byte cloning and safe fresh-clone guidance. Production provenance guards
  remain unchanged; no tests were removed. The rebuilt focused Debug scope passed
  16/16 (enrollment and both-provider Git round trips) in 1m22s, with no failures
  or skips. Its completed TRX is under
  `artifacts/clone-byte-repair-20261025/focused/`. The same rebuilt binaries passed
  full Debug verification 639/639 in 14m49s, with zero failures, skips, timeouts,
  or aborts. The completed report is under
  `artifacts/clone-byte-repair-20261025/debug/`; disposable reproduction evidence
  is in `artifacts/clone-byte-repair-20261025/reproduction.md`. Compilation and
  whitespace checks passed without warnings/errors.

Ferry reviewed and approved this repair plus its semantic commit, push, and new
manual Windows workflow run. Immediate next work: publish the reviewed checkpoint,
verify the remote commit, and inspect the explicitly dispatched Windows result.
Both local verification gates have passed, but milestone 18 stays open until
Windows execution and review pass. No unrelated implementation is authorized.
Provider-first CLI work remains deferred; full Release regression remains a
release gate rather than part of this pre-push Debug verification.

## v1.4.0-alpha.8 — Copilot Linux ARM64 lifecycle

- [x] Extend the existing isolated Linux ARM64 installer test instead of adding
  an overlapping workflow.
- [x] Add coverage for Copilot init, in-sync status, runtime-to-repository backup,
  and committed repository-to-runtime apply through the Native AOT executable.
- [x] Verify managed files, canonical/runtime byte equality, absence of unmanaged
  `config.json`, the backup commit subject, and unchanged `HEAD` across apply.
- [x] Isolate Git identity and commit signing from the runner configuration.
- [x] Keep Linux warning-marked because the test neither installs nor invokes the
  actual Copilot CLI harness.
- [x] Commit, push, and explicitly run the manual Linux ARM64 workflow: Native
  AOT build, command smoke checks, installer lifecycle, and complete isolated
  Copilot lifecycle passed in
  [run 36283284376](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36283284376).

Immediate next work: review the deferred v2 provider-first API migration as the
next separately approved increment.

## v1.4.0-alpha.7 — Copilot macOS Native AOT lifecycle

- [x] Extend the existing isolated macOS ARM64 installer test instead of adding
  an overlapping workflow.
- [x] Initialize disposable Codex and Copilot homes through the built Native AOT
  executable without accessing personal runtime state.
- [x] Exercise Copilot init, in-sync status, runtime-to-repository backup, and
  committed repository-to-runtime apply.
- [x] Verify canonical/runtime byte equality, the backup commit subject, and an
  unchanged Git `HEAD` across apply.
- [x] Isolate Git identity and signing from the runner and developer machine.
- [x] Keep macOS warning-marked because this test does not install or invoke the
  actual Copilot CLI harness.
- [x] Commit, push, and explicitly run the manual macOS ARM64 workflow: Native
  AOT build, command smoke checks, installer lifecycle, and complete isolated
  Copilot lifecycle passed in
  [run 36283284718](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36283284718).

Immediate next work: implement and validate the same isolated Copilot lifecycle
on Linux ARM64 before beginning the deferred v2 API migration.

## v1.4.0-alpha.6 — Copilot repository-to-runtime apply

- [x] Add explicit Copilot apply routing with `--provider=copilot` and the
  established `--copilot-home` resolution order.
- [x] Require canonical instructions to be one regular committed Git file whose
  working bytes exactly match the committed blob and selected repository binding.
- [x] Atomically replace only runtime `copilot-instructions.md`; create a missing
  runtime file with user-only permissions and avoid equal-byte rewrites.
- [x] Preserve canonical bytes, Git `HEAD`, and unrelated repository changes;
  create no commit and capture no runtime state.
- [x] Reject dirty/uncommitted/unsupported canonical sources, path mismatches,
  repository-contained runtime paths, and invalid provider flags before writing.
- [x] Teach provider init to migrate the exact official backup-only skill and
  document the apply direction in the bundled Copilot skill.
- [x] Extend the isolated Windows lifecycle to restore committed canonical bytes
  after runtime drift and verify that `HEAD` is unchanged.
- [x] Run the manual Windows x64 workflow: Native AOT build, command smoke checks,
  and the complete isolated init/status/backup/apply lifecycle passed in
  [run 36273405366](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36273405366).

Immediate next work: validate the complete lifecycle through the remaining
isolated macOS and Linux ARM64 workflows before the v2 migration.

## v1.4.0-alpha.5 — Copilot runtime-to-repository backup

- [x] Add explicit Copilot backup routing with `--provider=copilot` and
  `--copilot-home` resolution matching init/status.
- [x] Validate canonical/runtime managed blocks and repository bindings before
  changing the canonical file.
- [x] Capture exact runtime instruction bytes, stage only the Copilot canonical
  path, and create the fixed `Backup Copilot instructions` commit.
- [x] Preserve runtime bytes and timestamps; report `unchanged` when the
  canonical path already matches both runtime and `HEAD`.
- [x] Reject unrelated working-tree/index changes, missing runtime files,
  unsupported blocks, path mismatches, and invalid provider flags.
- [x] Teach provider init to migrate the exact official status-only skill and
  document the backup direction in the bundled Copilot skill.
- [x] Extend the isolated Windows lifecycle to capture runtime Copilot drift and
  verify the canonical bytes and commit subject.
- [x] Run the manual Windows x64 workflow: Native AOT build, command smoke checks,
  and the isolated init/status/backup lifecycle passed in
  [run 36272081560](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36272081560).

Immediate next work: implement Copilot `apply` as the repository-to-runtime
direction.

## v1.4.0-alpha.4 — Read-only Copilot status

- [x] Add explicit Copilot status routing with `--provider=copilot` and the
  established `--copilot-home` resolution order.
- [x] Compare canonical and runtime `copilot-instructions.md` bytes without
  mutation, returning 0 for `in_sync`, 2 for `different` or `missing`, and 1
  for validation errors.
- [x] Reject Codex-only flags and provider-less Copilot flags.
- [x] Teach the bundled Copilot skill the status contract and permit provider
  init to replace only the exact official initialization-only predecessor.
- [x] Add focused command, immutability, resolution, argument, help, and skill
  migration tests.
- [x] Extend the isolated Windows Native AOT lifecycle to require an `in_sync`
  Copilot status after provider initialization.
- [x] Validate Copilot status through the Windows x64 Native AOT lifecycle in
  [run 36272081560](https://github.com/ferrywlto/ai-env-as-code/actions/runs/36272081560).

Immediate next work: continue the separately recorded Copilot backup/apply
increments without starting the deferred v2 public API migration.

## v1.4.0-alpha.3 — Copilot Windows initialization smoke

- [x] Exercise Copilot initialization through the Windows x64 Native AOT artifact
  with isolated repository and Copilot-home paths.
- [x] Verify canonical/runtime equality, the managed block, bundled skill
  installation, and continued non-ownership of Copilot `config.json`.
- [x] Keep the Windows support matrix warning-marked because CI does not install
  or exercise the actual GitHub Copilot CLI harness.
- [x] Run the manual Windows x64 workflow: Native AOT build, command smoke checks,
  and the isolated Codex/Copilot installer lifecycle passed in
  [run 35529409643](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35529409643).

Immediate next work: continue Copilot provider operations while preserving the
warning-marked Windows harness status until the real CLI is available.

## v1.4.0-alpha.2 — Provider source-path alignment

- [x] Move bundled Codex skill sources to `skills/codex/aec/`.
- [x] Move bundled Copilot skill sources to `skills/copilot/aec/`.
- [x] Group provider-specific engine files under `src/Aec/Providers/<provider>/`.
- [x] Preserve namespaces, command syntax, canonical repository paths, embedded
  resource names, and installed `<provider-home>/skills/aec/` paths.

## Deferred v2 public API change

Shared-instruction composition and enrollment decisions now precede this work.
The provider-first syntax below remains a proposal pending that design review.

After shared-instruction contract review, introduce the breaking provider-first
command contract in a subsequent 2.0 alpha increment:

```text
aec codex init|status|backup|apply ...
aec copilot init|status|backup|apply ...
```

- [ ] Replace implicit Codex routing with explicit provider command groups.
- [ ] Replace `--codex-home` and `--copilot-home` with group-local `--home`.
- [ ] Move Codex uninstall and skill upgrade beneath the Codex command group.
- [ ] Remove legacy command forms without compatibility aliases.
- [ ] Update installers, generated uninstallers, skills, documentation, and tests.
- [x] Validate the complete Copilot lifecycle through isolated macOS ARM64,
  Windows x64, and Linux ARM64 workflows.

## v1.4.0-alpha.1 — Copilot macOS initialization

- [x] Define the native Copilot instruction contract at
  `copilot-instructions.md`, distinct from Codex `AGENTS.md`.
- [x] Add explicit `--provider=copilot` and `--copilot-home` initialization,
  resolving explicit path, `COPILOT_HOME`, then `~/.copilot`.
- [x] Capture or reconcile the canonical and runtime instruction files while
  preserving non-AEC bytes and rejecting path or skill conflicts.
- [x] Install the Copilot-compatible `skills/aec/SKILL.md` without creating
  Codex/OpenAI metadata paths.
- [x] Keep `config.json` outside AEC ownership and document the alpha boundary.
- [x] Add isolated xUnit coverage for initialization, resolution, repeatability,
  runtime reconciliation, and conflict rejection.
- [ ] Validate with a real local macOS Copilot CLI harness before changing the
  support matrix from ⚠.

Immediate next work: add Copilot `backup` and `apply` as separate directional
slices; do not add Copilot uninstallation or standalone skill upgrade yet.

## macOS Native AOT SDK selection

- [x] When `SDKROOT` is not explicitly supplied, resolve Xcode's macOS SDK through
  `xcrun` before Native AOT invokes `clang`.
- [x] Preserve a caller-provided `SDKROOT` for advanced or CI-specific toolchains.

## macOS ARM64 smoke workflow

- [x] Add a manual-only `macos-15` ARM64 workflow using built-in Git checkout.
- [x] Build the Native AOT executable and smoke-test `aec version` and
  `aec help`.
- [x] Run the existing isolated macOS installer/generated-uninstaller test.
- [x] Keep CI evidence separate from the existing real local Codex verification.
- [x] Commit, push, and explicitly run the workflow: Native AOT build, command
  smoke checks, and isolated installer/uninstaller test passed in
  [run 35412687899](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35412687899).

Immediate next work: choose the next local harness-provider slice; macOS ARM64
now has both real Codex dogfooding and repeatable CI regression evidence.

## Linux ARM64 build and smoke slice

- [x] Add a host-checked Ubuntu ARM64 Native AOT build script targeting
  `linux-arm64` and writing under ignored `artifacts/`.
- [x] Add a manual-only `ubuntu-24.04-arm` workflow to build and smoke-test
  `aec version` and `aec help` without accessing personal AEC data.
- [x] Document the build prerequisites and distinguish CI smoke from real
  Codex harness verification.
- [x] Commit, push, and explicitly run the Linux ARM64 workflow: Native AOT
  build, `aec version`, and `aec help` passed in
  [run 35409334463](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35409334463).
- [x] Add the Linux ARM64 installer with the per-user `$HOME/.local/bin`
  default, absolute custom-directory support, and no `PATH` mutation.
- [x] Generate `scripts/uninstall-aec-linux-arm64.sh` beside the installer;
  verify its original path and installed executable digest before cleanup.
- [x] Add an isolated Linux lifecycle test covering install, reinstall, custom
  path, tamper/copy refusal, real managed init/uninstall, and helper conflict.
- [x] Run the isolated Linux installer lifecycle: install, reinstall, repair,
  managed init, and uninstall passed in
  [run 35410432127](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35410432127).
- [ ] Later: validate with a real local Linux Codex harness before marking
  the matrix as verified.

Immediate next work: validate the installed executable against a real local
Linux Codex harness when one is available.

## Approved cross-platform decisions and Windows installer slice

- [x] Record the local-harness-only configuration scope; cloud-hosted use is
  outside the current scope.
- [x] Keep releases alpha/beta until the actual target harness has been tested.
- [x] Roadmap Codex on Windows/Linux first, then Copilot, Claude, and Gemini on
  macOS/Windows/Linux.
- [x] Keep GitHub Actions manual-only with `workflow_dispatch`; an explicitly
  authorized `gh workflow run` may trigger it, but pushes do not start CI.
- [x] Use isolated CI fixtures; never access the personal AEC data repository.
- [x] Record that `act` covers suitable Linux containers locally, not Windows
  x64 validation from an ARM Mac.
- [x] Add the Windows x64 Native AOT build script and manual-only workflow
  for `aec.exe version` and `aec.exe help` smoke checks.
- [x] Run the workflow on a Windows runner: build, `version`, and `help` passed
  in [run 35171142521](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35171142521).
- [x] Add `scripts/install-win-x64.ps1` to consume
  `artifacts/aec-win-x64/aec.exe`, default to the per-user
  `$env:LOCALAPPDATA\Programs\AEC` directory, and accept an absolute
  `-InstallDir` override.
- [x] Keep installation free of `PATH` and PowerShell execution-policy
  mutation; explain effective Codex `PATH` and the required Codex restart.
- [x] Generate `scripts/uninstall-aec-win-x64.ps1` beside the installer. It
  must run the exact installed binary's `aec uninstall` first, then remove the
  binary and helper script, with optional `-CodexHome`, while preserving the
  data repository and `config.toml`.
- [x] Add an isolated Windows runner test for install, reinstall, and uninstall.
- [x] Run the isolated Windows installer lifecycle test: install, reinstall,
  managed init, and uninstall passed in
  [run 35282576084](https://github.com/ferrywlto/ai-env-as-code/actions/runs/35282576084).
- [ ] Later: validate the installed executable against a real Windows Codex
  harness.

Immediate next work: validate against a real Windows Codex harness when one is
available. The isolated installer lifecycle has passed on a Windows runner.

## v1.3.1 — ChatGPT provider attachment

- [x] Accept the exact ChatGPT scaffold generated by `aec init --provider=chatgpt`.
- [x] Reject unexpected provider directories and ChatGPT scaffold entries.
- [x] Add an attachment regression test for a completed provider-enabled repository.
- [x] Build, install, dogfood, and apply the patch to the selected data repository.

## v1.3.0 — Managed AEC workflow guidance

- [x] Add compact provider-neutral v5 and provider-aware v6 managed blocks.
- [x] Preserve and recognize released v3/v4 blocks while migrating to v5/v6.
- [x] Preserve non-AEC bytes and existing provider-specific guidance.
- [x] Update README, bundled skill guidance, version metadata, and skill-upgrade predecessor support.
- [x] Pass focused managed-block and initialization validation.
- [x] Run the deferred two-checkout Git round-trip rehearsal through ticket #2
  milestone 16; managed Codex/Copilot cases pass on one host/platform.

Immediate next work: rehearse push/pull through a disposable local bare Git remote,
then document the cross-machine workflow before adding remote convenience commands.

## GitHub Pages landing page

- [x] Create a dependency-free, responsive landing page under `docs/`.
- [x] Explain AEC directionality, source-of-truth model, current platform, and installation.
- [x] Add the public roadmap for platform and provider support.
- [x] Add GitHub, MIT licence, citation, and PayPal coffee links.
- [x] Explicitly enable GitHub Pages and verify its deployed public URL.

## v1.2.3 — Security containment

- [x] **P1:** Reject linked path ancestors and repository-contained Codex runtime paths in `status` and `backup`.
- [x] **P1:** Disable Git replacement objects throughout `BackupCommand` validation and commit operations.
- [x] Add focused regression tests for both P1 boundaries.
- [x] Pass build, focused tests, the full Debug suite, and diff validation.
- [x] Pass formatting after resetting the stale .NET build servers.

Immediate next work: decide whether to reset the stale .NET build servers and retry
formatting; do not begin P2 work.

## Test scope isolation roadmap

- [x] Document the current five-minute full-suite expectation and quiet VSTest output.
- [x] Make focused class-filtered tests the normal per-change workflow.
- [x] Reserve the full Debug and Release suites for pre-push and pre-release regression gates.
- [x] Classify tests as unit, command integration, filesystem safety, or end-to-end.
- [x] Add stable xUnit traits or separate test projects for those scopes.
- [x] Measure test durations and prioritize proven bottlenecks before optimization.
- [x] Remove process-state serialization only where isolated execution is demonstrated.
- [x] Preserve full-suite regression coverage while making focused feedback faster.

## Security review implementation backlog

- [x] **P2:** Stage verified blobs without executing configured clean filters or optional Git integrations.
- [x] **P2:** Reject linked worktrees and Git metadata outside the selected data repository.
- [x] **P2:** Prevent fresh-init identity cleanup from deleting concurrently created repository state.
- [x] **P2:** Close or explicitly constrain pathname-based file validation and mutation races.
- [x] **P3:** Verify the installed executable digest before generated-uninstaller execution and deletion.
- [x] **P3:** Create a missing runtime `AGENTS.md` with user-only permissions.

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
- [x] Publish v1.0 publicly after the private dogfood period concludes.

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
