---
name: aec
description: Operate AI Environment as Code data repositories through explicit init, status, backup, and apply flows. Use when the user invokes `$aec`, names an `aec` command in chat, asks to initialize, inspect, back up, or apply an AEC repository, or requests a change to personal Codex instructions or configuration managed by AEC.
---

# AEC

Always use the explicitly selected AEC data repository. If none was selected, ask
for it. Pass its exact absolute path through `--repo`; never infer the data
repository from the current directory, executable location, skill directory, or
engine repository.

Invoke `aec` directly. If it is unavailable, report that the CLI is not installed
or not on `PATH` and stop. Do not locate, build, or run an engine checkout as a
fallback. Use only the following operations.

## Inspect

- `aec help` lists supported command forms without changing state.
- `aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` compares the
  canonical and runtime environment without changing either. It compares exact
  `AGENTS.md` bytes and the managed root `personality` semantically, ignoring
  unrelated runtime TOML. Treat `different` and `missing` as detected drift, not
  command failure.

If the requested data-flow direction is unclear, run `status` and ask the user to
choose. Never invent an automatic `sync` operation.

## Initialize or attach

Run ordinary initialization as:

```text
aec init --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
```

For a missing or empty repository, require a runtime `AGENTS.md`. The command reads
the runtime personality and warns before enrolling `none` when that root value or
`config.toml` is missing. It installs the bundled skill, commits the exact runtime
instructions and managed personality as `Backup Codex environment`, commits the
reconciled AEC instructions as `Initialize AEC instructions`, then applies both
committed managed files. The managed runtime files remain untouched until both
commits succeed. A recognizable current two-file or legacy AGENTS-only baseline
resumes this lifecycle without rewriting its existing root commit; legacy resume
adds canonical config in the second commit. An explicit ordinary `init` request
authorizes this fixed flow; do not wrap it in separate `backup` or `apply` commands.

For a completed pulled repository whose recorded path matches `--repo`, ordinary
`init` installs the skill and applies the committed canonical `AGENTS.md` and
managed personality. Either runtime file may be missing. This flow does not back up
runtime or create a commit. A completed legacy repository without committed
canonical config fails closed; do not invent or capture a value to bypass it.

If ordinary `init` reports that the path recorded in committed
`environment/providers/codex/AGENTS.md` differs from `--repo`, show the user both
paths and ask for explicit confirmation. Do not mutate anything or pass
`--force-path-change` before that confirmation. After confirmation, run:

```text
aec init --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH] --force-path-change
```

This installs the skill, updates the existing v3 or v4 block, commits only canonical
`AGENTS.md` as `Rebind AEC repository path`, and applies both committed managed
files. Never use the flag for a fresh or partial repository, provider
initialization, or `apply`.

No `init` form pushes Git commits.

Initialize manual ChatGPT backup files only with:

```text
aec init --repo ABSOLUTE_PATH --provider=chatgpt
```

Do not pass `--codex-home`. If provider initialization reports a recorded-path
mismatch, use the ordinary confirmed-init flow first.

## Back up runtime to Git

Run `aec backup --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` only after the
user authorizes the repository write and Git commit. It copies exact runtime
instructions and an existing supported root personality to their canonical files,
then commits both as `Backup Codex environment` when needed. If the runtime
personality is missing, it warns and stops instead of adding a default. It never
writes runtime, captures unrelated runtime TOML, or pushes.

## Apply Git to runtime

Run `aec apply --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` only after the
user authorizes runtime replacement. It writes the exact committed `AGENTS.md`,
creating the runtime file when missing, and adds or updates only the committed root
personality, creating `config.toml` when missing. It preserves unrelated runtime
TOML and warns before adding a missing managed value. It never captures runtime
changes, changes Git, commits, or pushes. If it reports that the committed AEC path
differs from `--repo`, stop and prompt the user to run ordinary `aec init` instead.
