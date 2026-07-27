---
name: aec
description: Operate AI Environment as Code data repositories through explicit init, status, backup, and apply flows. Use when the user invokes `$aec`, names an `aec` command in chat, asks to initialize, inspect, back up, or apply an AEC repository, or requests a change to a personal Codex `AGENTS.md` managed by AEC.
---

# AEC

Use the explicitly selected AEC data repository. If no repository or initialization
target was selected, ask for it. Pass its exact absolute path through `--repo` to
`init`, `status`, `backup`, and `apply`. Never infer the data repository from the
current directory, executable location, skill directory, or engine repository.

Invoke `aec` directly. If it is unavailable, report that the CLI is not installed
or not on `PATH` and stop. Do not locate, build, or run an engine checkout as a
fallback.

Use only these operations:

- `aec help` lists the supported command forms without changing state.
- `aec init --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` requires an
  existing runtime `AGENTS.md`. It creates a missing or empty data repository, or
  resumes only a recognizable baseline-only partial initialization. It commits the
  exact runtime as `Backup Codex AGENTS.md`, commits the reconciled AEC source as
  `Initialize AEC instructions`, then applies that committed source. It does not
  push.
- `aec init --repo ABSOLUTE_PATH --provider=chatgpt` initializes only the manual
  ChatGPT files in an existing data repository. Do not pass `--codex-home`.
- `aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` compares the
  canonical and runtime bytes without changing either. Treat `different` and
  `missing` as detected drift, not command failure.
- For runtime → repository flow, run
  `aec backup --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` only after the user
  authorizes the repository write and Git commit. It copies runtime to the canonical
  source and commits that source when needed. It never writes the runtime or pushes.
- For committed repository → runtime flow, run
  `aec apply --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` only after the user
  authorizes runtime replacement. It never captures runtime changes, mutates Git,
  commits, or pushes.

If the requested direction is unclear, use `status` and ask the user to choose.
Never invent an automatic `sync` operation or infer a mutation direction.

An explicit ordinary `init` request authorizes its fixed two-commit and apply
sequence. Do not wrap it in separate `backup` or `apply` invocations.
