# Shared instructions — 2.0 development direction

Status: approved direction; implementation remains incremental on
`development/2.0`.

```mermaid
flowchart LR
    Shared[Shared policy] --> Render["aec render"]
    Platform[Selected platform policy] --> Render
    Provider[Enrolled provider overlays] --> Render
    Render --> Target[Portable canonical target]
    Target -.->|future integration| Apply["aec apply"]
    Legacy[Committed 1.x provider file] --> Apply
    Apply --> Runtime[Local harness]
```

Git history remains the source of truth. Today, `render` only updates repository
targets; `apply` still reads the legacy provider file. Equivalent instructions
across harnesses do not guarantee identical model behaviour.

## Choose the authored source

```mermaid
flowchart TD
    Request[Requested instruction change] --> Scope{Where should it apply?}
    Scope -->|Every enrolled harness and platform| Shared[Shared instructions]
    Scope -->|This platform's paths or permissions| Platform[Platform policy]
    Scope -->|One harness's mechanics| Provider[Provider overlay]
    Scope -->|AEC repository binding or command guidance| Control[AEC-generated block]
    Shared --> Review[Show affected enrolled targets for review]
    Platform --> Review
    Provider --> Review
    Control --> Review
```

| Source | Contents | Owner |
|---|---|---|
| `environment/shared/instructions.md` | Collaboration, approval, engineering, learning, documentation, agile and architecture preferences; general parallel execution policy | User |
| `environment/platforms/<platform>/policy.md` | Explicitly approved local access roots and platform-specific personal rules | User |
| `environment/providers/<provider>/overlay.md` | Harness-specific execution preferences, including explicitly selected model mappings | User |
| AEC control block | Supported skill invocation, repository binding, canonical paths and directional command guidance | AEC |

Composition order is AEC block → shared → platform → provider. Later sections
cannot silently override shared approval or access restrictions.

For current Codex content, shared policy owns general personal rules; platform
policy owns the approved folder-access rule; the Codex overlay owns model mapping
and worker mechanics. Do not invent Copilot mappings or discard unclassified text.

## Lazy enrollment

```mermaid
flowchart TD
    Legacy[Completed ordinary init and committed provider baseline] --> Split[Review and commit shared, platform and provider sources]
    Split --> Select[Select the current platform and provider]
    Select --> Init{Explicit init --enroll-shared?}
    Init -->|Yes| Create[Create only the selected portable target]
    Init -->|No| None[Create no target]
    Pulled[Provider source pulled from another machine] -.->|not local enrollment| Init
    Create --> Keep[Preserve existing data and manual ChatGPT backups]
```

The intended v2 macOS ARM64 Codex source set contains only:

```text
environment/
├── shared/instructions.md
├── platforms/macos-arm64/policy.md
└── providers/codex/overlay.md
```

The current opt-in requires these files to exist in a completed repository
and match committed `HEAD`. It does not create or classify them. Existing Codex
`AGENTS.md` and managed `config.toml` remain the active legacy files;
initializing Copilot or another platform enrolls only the selected target.
CI coverage does not enroll anything in the user's data repository.

## Alignment workflow

```mermaid
sequenceDiagram
    participant User
    participant Skill as Installed AEC skill
    participant Repo as Data repository
    participant CLI as AEC binary
    User->>Skill: Request an instruction change
    Skill->>User: Propose source and affected targets
    User->>Skill: Approve source edit
    Skill->>Repo: Edit authored source
    Skill->>CLI: aec render --repo PATH
    CLI->>Repo: Regenerate enrolled current-platform targets
    Skill->>User: Show source and rendered diff
    User->>Skill: Approve commit separately
    Skill->>Repo: Commit reviewed files
    Note over User,CLI: Runtime apply remains a later 2.0 increment
```

Both installed skills use the same source decision; the binary validates and
renders it. `render` reports changed paths but never commits, applies, pushes,
or writes runtime. After future integration, an explicit `apply` will deploy
committed targets and `status` will compare them with runtime. There is no
automatic `sync`.

The v2 target path is
`environment/targets/<platform>/<provider>/<instruction-file>`. It contains
portable authored sections. AEC's machine-specific repository binding and
command guidance belong in a generated local runtime block, not the portable
target. Existing `environment/providers/<provider>/<instruction-file>` files
remain the active canonical paths until a reviewed migration.

`init --enroll-shared` now creates the selected target after validating the
three committed authored sources and the completed legacy AEC repository. It
does not edit runtime, stage, commit or push. `render` still treats existing
target directories as enrollment. An internal repository-only validator now
checks one explicitly selected platform/provider target against the three
committed authored sources and rejects dirty working bytes. It is not wired to
runtime commands. An internal read-only status helper now requires an explicit
local provider home, checks any runtime instruction file's AEC repository
binding, and compares it byte-for-byte with the committed target plus the
generated local control block. A missing runtime instruction file reports
`missing`; a missing provider home or wrong binding stops. It never treats a
pulled target as proof that its harness is installed, and it is not wired to
the public `status` command.

In-memory 2.0 local control blocks now name the committed target for the
selected platform and direct edits to the authored shared, platform, or
provider source before `render` and review. Codex uses strict block versions
7/8 (the latter retains manual ChatGPT backup guidance); Copilot uses version
2. The read-only helper recognizes these versions and the supported 1.x blocks
without changing the public 1.x generators. The portable target itself never
stores the machine-specific repository binding.

Planned reverse mapping for `backup` is deliberately narrower than free-form
instruction editing:

```mermaid
flowchart TD
    Drift[Runtime differs from committed rendered baseline] --> Check{Exactly one authored section changed?}
    Check -->|No| Stop[Stop for review; do not guess a source]
    Check -->|Yes| Fresh{Baseline still current?}
    Fresh -->|No| Stop
    Fresh -->|Yes| Source[Update that section's authored source]
    Source --> Render[Regenerate every enrolled consuming target]
    Render --> Commit[Review and approve the backup commit]
```

Exact AEC-owned delimiters distinguish shared, platform and provider text.
Marker collisions, generated-block edits, outside text, multi-section changes,
and stale baselines stop. Safe reverse mapping is **not implemented yet**.

## Direction and boundaries

- `status` inspects; `backup` captures runtime into the repository; `apply`
  deploys committed repository content. Preserve these separate directions.
- Shared-source changes should propagate through deterministic composition.
- Do not infer personal access permissions from installation paths.
- Do not treat provider directory presence as final proof of local harness
  enrollment: a pulled repository can contain providers used only elsewhere.
- A selected local provider home and supported runtime AEC binding establish
  the scope for an existing runtime file. An absent runtime file is `missing`,
  not evidence of activation; an absent home or wrong binding stops. Repository
  target presence alone never establishes local installation.
- Keep the public `status` on the 1.x source until directional commands can
  migrate coherently. Changing `status` alone would report drift that the
  current `apply` could not fix; `backup` also still writes the legacy source.
- Platform policy is sufficient for the current single-machine case. Different
  machines on the same platform may need distinct access roots; defer their
  storage contract until required, rather than claiming platform equals machine.
- Keep CLI migration and multi-target `--all` semantics deferred until those
  contracts are reviewed. Previously illustrated command forms are proposals.

## Implemented foundation and next boundary

```mermaid
flowchart LR
    Compose[In-memory composition] --> Sections[Exact source section markers]
    Sections --> Render[Repository-only render]
    Render --> Enrollment[Opt-in target enrollment]
    Enrollment --> Validation[Committed target validation helper]
    Validation --> Local[Explicit local home and runtime binding]
    Local --> Block[Generated local block, v2 markers]
    Block --> Inspect[Internal read-only target status]
    Inspect -.->|later| Directional[Coherent CLI migration]
```

A small .NET BCL composition function accepts explicitly supplied shared,
platform, provider and generated-block content. Its preview is in memory and
does no enrollment, runtime write, commit or CLI migration. Focused xUnit tests
cover stable ordering, empty optional content, line endings and preservation of
personal text in disposable fixtures; the personal data repository is untouched.

The composition function uses two LF characters between nonempty sections,
independently of the operating system. All source characters, including trailing
newlines, CRLF, indentation and Unicode, remain unchanged. Empty sections are
omitted; whitespace-only content is retained. It adds no final newline. Optional
platform/provider text may be absent. The caller supplies the generated block;
this function neither validates that block nor interprets Markdown or resolves
policy conflicts. The repository-only render command now wraps authored sections
with exact markers and validates strict UTF-8. Runtime integration and safe
reverse mapping through `backup` remain separate increments.

The approved rendered Codex/Copilot example confirms that shared approval and
access policy are retained while provider-only instructions stay scoped. The
internal validator checks a selected target's committed files and exact rendered
bytes without reading runtime. The internal status helper can compare that
target with a selected local runtime and supports both old and new generated
blocks. It does not alter public `status` or `apply`. Existing provider-specific
canonical files remain the active CLI source until a reviewed directional
migration.
