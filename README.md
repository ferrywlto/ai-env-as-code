# AI Environment as Code

Version 0.5 contains the first incremental slices of a minimal .NET tool for
creating a source-of-truth data repository, comparing its Codex instruction file
with a local runtime target, and moving approved changes in either explicit direction.

## Version history

| Version | Increment |
|---|---|
| 0.1 | Project initialization |
| 0.2 | `init` command |
| 0.3 | `backup` command |
| 0.4 | `init` AEC instruction injection and update |
| 0.5 | `apply` command |

The project and CLI report the current release as `0.5.0`.

## apply

```text
aec apply --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
```

`apply` has the opposite data-flow direction from `backup`:

```text
committed <repo>/environment/providers/codex/AGENTS.md
  -> <codex-home>/AGENTS.md
```

The repository must be the exact root of a non-bare Git working tree with a
committed `HEAD`. Detached HEAD is accepted because `apply` does not create a commit.
The canonical path must be a committed regular Git file with no staged or unstaged
changes, and its raw working bytes must exactly match the committed blob. This
rejects line-ending, clean/smudge, and other Git filters that could make visibly
different bytes appear clean. Unrelated repository changes are ignored and untouched.

When runtime bytes differ or the runtime file is absent, `apply` writes a flushed
temporary file beside the target, confirms the observed runtime has not changed,
moves the file into place, and verifies the resulting bytes. A runtime target inside
the data repository is rejected. The command writes `applied` after a successful
write or `unchanged` when no write is needed; both return exit code 0.

Invocation itself authorizes replacing observed runtime drift. There is no second
confirmation flag, separate backup, receipt, Git mutation, commit, or push.

## backup

```text
aec backup --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
```

`backup` has one explicit data-flow direction:

```text
<codex-home>/AGENTS.md
  -> <repo>/environment/providers/codex/AGENTS.md
  -> Git commit in <repo>
```

The repository must be the exact root of a non-bare Git working tree on a symbolic
branch. Before changing the canonical source, the command rejects staged, unstaged,
or untracked changes anywhere else in the repository. A pending change to the
canonical source itself is allowed: the runtime file replaces it, or an already
equal staged copy is committed. This lets a rerun resume after a prior commit
failure.

When the runtime bytes differ, the canonical source is replaced through a flushed
temporary file in the same directory and read back for verification. The command
then stages only the fixed canonical path and creates a commit with this subject:

```text
Backup Codex AGENTS.md
```

Git filters that would change the bytes while staging are rejected before commit.
Configured Git hooks are isolated from the backup commit so they cannot alter the
verified bytes or fixed subject; both are checked again after the commit.
On success, the command writes `committed <full-sha>`. When the runtime, working
file, index, and current commit already agree, it writes `unchanged`. Both outcomes
return exit code 0; validation and Git failures return exit code 1.

There is no separate backup directory or backup manifest. Git commit history is the
source of truth. This increment does not push, write the runtime target, or implement
the inverse `restore` direction.

## init

```text
aec init [directory] [--codex-home ABSOLUTE_PATH]
```

`init` creates a new data repository in a missing directory or an existing empty
directory. When the operand is omitted, the current working directory is used. A
relative operand is resolved from the current working directory.

`--codex-home` selects the runtime root. When omitted, `init` uses a non-empty
`CODEX_HOME` and then `~/.codex`, matching `status` and `backup`. The Codex home must
already exist, but its `AGENTS.md` may be absent.

Any existing entry—including a hidden file or `.git`—makes the target non-empty and
causes the command to fail before making changes. This makes `init` intentionally
one-shot: running it a second time against the repository it created is an error.
Direct symbolic-link targets and paths containing symbolic-link components are also
rejected. Runtime validation and instruction merging complete before the repository
is created.

The AEC-managed instruction block is delimited and versioned explicitly:

```markdown
<!-- AEC:BEGIN version=1 -->
## AI Environment as Code

Treat the AEC data repository's Git commit history as the source of truth.
Preserve instructions outside this managed block.
Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.
<!-- AEC:END -->
```

When no block exists, `init` inserts it at the logical top of the runtime file,
after an optional UTF-8 byte-order mark, followed by one blank separator line and
the existing instructions. An older version is replaced in place. A current
version is retained byte-for-byte. Duplicate, malformed, or newer-version markers,
invalid UTF-8, NUL bytes, and merged content over 1 MiB are rejected.

On success, Git is initialized with `main` as the initial branch. The merged bytes
are written to both the runtime target and this canonical source:

```text
environment/providers/codex/AGENTS.md
```

All bytes outside a replaced block are retained. The canonical and runtime files
therefore begin in sync. The command does not stage files or create a commit; run
`backup` separately to create the initial Git commit.

## status

The `status` command is read-only:

```text
aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
```

`--repo` is required and identifies the source-of-truth data repository. The tool
reads this fixed source:

```text
<repo>/environment/providers/codex/AGENTS.md
```

`--codex-home` identifies the observed runtime root. When omitted, the tool uses a
non-empty `CODEX_HOME` environment variable and then falls back to `~/.codex`. The
observed target is always:

```text
<codex-home>/AGENTS.md
```

Explicit paths and `CODEX_HOME` must be absolute. For `status`, the executable's
location and the current working directory are never treated as the data repository.

The command compares the exact bytes without changing either file. It writes one
status to standard output:

| Status | Meaning | Exit code |
|---|---|---:|
| `in_sync` | Source and target bytes are equal | 0 |
| `different` | Source and target bytes differ | 2 |
| `missing` | Codex home exists but its runtime target is absent | 2 |

Invalid arguments, missing roots or canonical data, symbolic links, directories,
files over 1 MiB, and file-system failures write a diagnostic to standard error and
return exit code 1.

## Build, test, and run

The production project uses only the .NET 10 Base Class Library. The test project
uses xUnit and has no coverage dependency.

```bash
dotnet build src/Aec/Aec.csproj
dotnet test tests/Aec.Tests/Aec.Tests.csproj
dotnet run --project src/Aec/Aec.csproj -- --version
dotnet run --project src/Aec/Aec.csproj -- \
  init /path/to/new-data-repository \
  --codex-home /absolute/path/to/codex-home
dotnet run --project src/Aec/Aec.csproj -- \
  status \
  --repo /absolute/path/to/data-repository \
  --codex-home /absolute/path/to/codex-home
dotnet run --project src/Aec/Aec.csproj -- \
  backup \
  --repo /absolute/path/to/data-repository \
  --codex-home /absolute/path/to/codex-home
dotnet run --project src/Aec/Aec.csproj -- \
  apply \
  --repo /absolute/path/to/data-repository \
  --codex-home /absolute/path/to/codex-home
```

## Current boundaries

The tool does not implement `diff`, `verify`, `restore`, rendering, manifests,
multiple providers, or pushing.

Source writes and Git commits operate on the data repository explicitly supplied
through `--repo`; they never infer or modify the engine repository.
