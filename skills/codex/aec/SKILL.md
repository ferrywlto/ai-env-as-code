---
name: aec
description: Operate AI Environment as Code through explicit init, status, backup, apply, version, uninstall, and installed-skill upgrade flows. Use when the user invokes `$aec`, names an `aec` command, asks to initialize, inspect, back up, apply, upgrade, or uninstall AEC, or requests a change to personal Codex instructions or configuration managed by AEC.
---

# AEC

For repository operations, always use the explicitly selected AEC data repository.
If none was selected, ask for it. Pass its exact absolute path through `--repo`;
never infer the data repository from the current directory, executable location,
skill directory, or engine repository.

Invoke `aec` directly. If it is unavailable immediately after installing the CLI
or adding its directory to `PATH`, fully restart Codex and retry: its process
inherits `PATH` when the app starts. If it remains unavailable, report that the CLI
is not installed or not on `PATH` and stop. Do not locate, build, or run an engine
checkout as a fallback. Use only the following AEC operations; Git transport
is separate and governed below.

## Inspect

- `aec help` lists supported command forms without changing state.
- Run exactly `aec version` to report the installed executable version without
  changing state. It accepts no options or repository path; do not use
  `aec --version`.
- `aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]` compares the
  canonical and runtime environment without changing either. It compares exact
  `AGENTS.md` bytes and the managed root `personality` semantically, ignoring
  unrelated runtime TOML. Treat `different` and `missing` as detected drift, not
  command failure.

If the requested data-flow direction is unclear, run `status` and ask the user to
choose. Never invent an automatic `sync` operation.

## Git remote preflight

`--repo` selects a local data repository, not a hosting service or remote. Before
an approved canonical write (including init, backup, render, or a manual edit)
or runtime apply, inspect that repository's Git state. `aec status` remains a
local-only comparison. Include any fetch or fast-forward in the plan for the
user's approval. For an existing remote-backed repository, use the
current branch's upstream to identify the remote; query its advertised `HEAD`
for the default branch. Never assume `origin`, `main`, GitHub, or GitLab. If
the upstream or default branch is ambiguous or unavailable, stop and ask. If
either the current local branch or its upstream branch is not the advertised
remote default branch, warn and pause for confirmation; never switch branches.

With a clean worktree on the default branch, fetch and fast-forward only from
that upstream before changing canonical data or applying it. If the worktree is
dirty, the local branch is ahead or diverged, or the remote is unreachable,
stop and report the state; never merge, rebase, or force-push automatically.
If no remote exists, including before a new repository is initialized, allow
the approved local AEC operation but warn that its commits are not available
on another machine.

After an approved canonical commit, request separate authorization to push the
current branch to that same remote. Show its resolved push destination and ask
if it differs from the fetch destination or is ambiguous. Verify publication
before applying a routine remote-backed instruction edit. Push only that branch
with an explicit destination, not Git's configurable default refspec. If push
fails, stop without applying.
Do not push merely because the user requested `backup`, `render`, or `init`.
Ordinary Codex `init` is an exception to publication-before-apply: its fixed
internal lifecycle applies after local commits, before an external push can occur.
Report that limitation and ask before publishing its commits.

## Shared instructions (2.0 development)

Help the user classify each rule by scope: all enrolled harnesses belong in shared
instructions, local paths or OS rules in platform policy, and one harness's
mechanics in its provider overlay. Never infer this split from an existing
`AGENTS.md`; show a proposed split for review first.

After the user has reviewed and committed all three authored source files,
explicitly enroll Codex on the current platform only when authorized:

```text
aec init --repo ABSOLUTE_PATH --enroll-shared [--codex-home ABSOLUTE_PATH]
```

This creates only the portable Codex target. It does not edit runtime, commit,
push, or replace the legacy init flow. Review the target before a separate
commit. For later approved source changes, show affected target files, then
run `aec render --repo ABSOLUTE_PATH` to regenerate the current platform's
enrolled canonical targets; review the source and target changes together and
obtain explicit approval before committing them.
`render` changes no runtime and creates no commit. The current `apply` and
`backup` commands still use their 1.x canonical paths, so do not claim a rendered
target was deployed. If v2 target enrollment is absent, stop this workflow and
explain that cross-harness deployment is not available yet.

## Upgrade installed skill guidance

After the latest AEC executable has been built and installed, run only with the
user's explicit authorization:

```text
aec skill upgrade [--codex-home ABSOLUTE_PATH]
```

The user may run the command directly or explicitly ask you to run it. Never infer
authorization merely because a newer release may exist. Do not pass `--repo`; this
operation replaces only exact recognized official versions of the installed
`skills/aec/SKILL.md` and `skills/aec/agents/openai.yaml`. It never updates the
executable, repository data, runtime instructions or config, or other skills. After
success, continue normally. Do not require an app restart or a new task for this
skill-only update; the next request that selects `$aec` reads the updated installed
guidance.

## Uninstall

Run only with the user's explicit authorization:

```text
aec uninstall [--codex-home ABSOLUTE_PATH]
```

Do not pass `--repo`. The command removes the recognized AEC block from runtime
`AGENTS.md` and removes only the exact recognized official `$aec` skill files. It
preserves instructions outside the block, all of `config.toml`, the executable,
unrelated skills, and every AEC data repository. If an unmanaged `$aec` reference
remains outside the block or a managed skill file was customized, it stops without
mutation. A repeated successful invocation reports `unchanged`.

For a complete personal installation cleanup, run the generated script located
beside the installer in the engine checkout:

```text
./scripts/uninstall-aec.sh [--codex-home ABSOLUTE_PATH]
```

Run it only with explicit authorization. It invokes the exact executable selected
during the latest installation, so it does not depend on `PATH`. It first runs
`aec uninstall`; only after that succeeds does it remove that executable and the
generated script itself. It preserves `config.toml` and every AEC data repository.
If the user installed another copy later, this script follows that latest selected
copy; any earlier executable remains their separate file.

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

This installs the skill, updates an existing supported v3 through v6 block, commits only canonical
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

Initialize the local GitHub Copilot CLI provider only with:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

The selected Copilot home is explicit `--copilot-home`, then absolute
`COPILOT_HOME`, then `~/.copilot`. This alpha command creates or reconciles the
native `copilot-instructions.md`, its canonical provider copy, and the Copilot
`skills/aec/SKILL.md`. It does not manage `config.json`, stage, commit, push, or
invoke Copilot. Copilot `status`, `backup`, and `apply` require the same explicit
`--provider=copilot` selection; Copilot `uninstall` and skill upgrade are not yet
available. Do not substitute Codex commands or flags.

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
