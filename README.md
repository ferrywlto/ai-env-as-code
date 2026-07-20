# AI Environment as Code

This repository contains the first incremental slices of a minimal .NET tool for
creating a source-of-truth data repository and comparing its Codex instruction file
with a local runtime target.

## Increment 2: init

```text
aec init [directory]
```

`init` creates a new data repository in a missing directory or an existing empty
directory. When the operand is omitted, the current working directory is used. A
relative operand is resolved from the current working directory.

Any existing entry—including a hidden file or `.git`—makes the target non-empty and
causes the command to fail before making changes. This makes `init` intentionally
one-shot: running it a second time against the repository it created is an error.
Direct symbolic-link targets and paths containing symbolic-link components are also
rejected.

On success, Git is initialized with `main` as the initial branch and this empty
canonical source file is created:

```text
environment/providers/codex/AGENTS.md
```

The command does not copy runtime instructions, stage files, or create a commit.

## Increment 1: status

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
dotnet run --project src/Aec/Aec.csproj -- init /path/to/new-data-repository
dotnet run --project src/Aec/Aec.csproj -- \
  status \
  --repo /absolute/path/to/data-repository \
  --codex-home /absolute/path/to/codex-home
```

## Current boundaries

The tool does not implement `diff`, `backup`, `apply`, `verify`, `restore`, rendering,
manifests, multiple providers, source capture, Git staging or commits, or live
configuration writes.

Future source writes and Git commits will operate on the data repository explicitly
supplied through `--repo`; they will never infer or modify the engine repository.
