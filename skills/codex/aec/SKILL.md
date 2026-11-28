---
name: aec
description: Use for AEC commands or changes to AEC-managed personal Codex instructions and configuration.
---

# AEC

Require an explicitly selected absolute data-repository path for `--repo`;
never infer it from the engine checkout, current directory, executable, or
skill. Invoke `aec` directly. If it is missing just after installation or a
`PATH` change, restart Codex and retry once. If still missing, stop; do not
build or run the engine checkout as a fallback. Git transport for ordinary commands is separate from AEC.
The migration command has the bounded retrieval behavior described below.
Versioned AEC instruction blocks may contain historical 2.0 command names.
Use the v3 commands in this skill when a block shows older syntax.

## Inspect

- `aec help` lists supported command forms without changing state.
- Run exactly `aec version` without options, not `aec --version`.
- `aec codex status --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]` is read-only.
  It compares the enrolled committed target with local instructions and the
  managed root `personality` semantically, ignoring unrelated runtime TOML.
  `different` and `missing` mean drift, not command failure.

If the requested data-flow direction is unclear, run `status` and ask the user to
choose. Never invent an automatic `sync` operation.

## Git remote preflight

For ordinary commands, before an AEC canonical write (`init`, `backup`,
`render`, or a manual edit)
or runtime `apply`, inspect Git state and use `aec codex status` for an existing
enrolled provider to identify local drift and confirm direction. Skip status
for a new repository without a baseline. Do not run preflight on unrelated
replies. `aec codex status` never checks the remote.

For a remote-backed repository, identify the remote from the current branch's
upstream and query its advertised default-branch `HEAD`; assume neither
`origin` nor `main`. Pause if tracking/default-branch identity is ambiguous,
either local or upstream branch is not that default, or pre-existing worktree
changes could mix with this operation. Never switch branches or silently
absorb/discard changes.

Compare local `HEAD` with the advertised tip without fetching. Equal means no
fetch; different does not establish which is newer. If different, seek
approval to fetch and, only when clean and strictly behind, fast-forward from
that upstream. Stop on ahead/diverged/ambiguous state. An unreachable remote
means freshness is unknown: stop. With no remote, permit approved local work
but warn it is not shared. Ordinary commands never automatically pull, merge,
rebase, or force-push.

After a canonical commit, obtain separate push approval. Resolve and show the
push destination; ask if it differs from the fetch remote or is ambiguous.
Push only the current branch to an explicit destination and verify publication
before routine remote-backed `apply`; stop if push fails. `backup`, `render`,
and `init` do not themselves authorize pushing. Ordinary Codex `init` applies
its local commits before an external push can be approved; report this exception.

## Migrate a 1.x data repository

Only with explicit authorization for repository commits, bounded retrieval,
and runtime apply, run from a separate terminal after closing affected harness
sessions and pausing other writers:

```text
aec migrate 2.0 --repo ABSOLUTE_PATH
```

Migration discovers enrolled local Codex and Copilot homes through
`CODEX_HOME` and `COPILOT_HOME`, then their default home paths. Both selected
providers may be affected. It requires a tracked default-branch remote and
may fetch and fast-forward; it never pushes. Inspect the selection and preserve
personal data before authorization. Do not run migration inside a harness
being migrated. Use a plain editor to review and fill the prepared sources;
do not infer their classification. Enter exactly `DONE` only when the user has
reviewed them. EOF or cancellation stops before completion. Inspect retained
commits, files, staging and runtime before starting a fresh invocation. A fresh
invocation starts at preflight and may require a new Preserve/Overwrite choice.

## Shared instructions

Propose the source of each rule for review: shared across enrolled harnesses,
platform-specific paths/OS policy, or Codex-only overlay. Never infer a split
from an existing `AGENTS.md`.

After all three authored sources are reviewed and committed, enroll Codex on
the current platform only with authorization:

```text
aec codex init --repo ABSOLUTE_PATH --enroll-shared [--home ABSOLUTE_PATH]
```

Enrollment creates only the portable target: no runtime write, commit, or
push. Review it before a separate commit. For later approved source changes,
show affected targets, run `aec render --repo ABSOLUTE_PATH`, then review and
separately approve the source/target commit. `render` does not touch runtime
or Git history. Codex `status`, `backup`, and `apply` now require the enrolled
committed target. Stop if v2 enrollment is absent; do not fall back to the
1.x canonical instruction file.

## Bundled AEC skill maintenance

The AEC install script refreshes enrolled bundled skills for the selected Codex
and Copilot homes. For a separate refresh with the installed executable, run
`aec upgrade` only with explicit authorization. It accepts no flags and selects
both homes through `CODEX_HOME` and `COPILOT_HOME`, falling back to their defaults.
It skips absent enrollments and checks both bundles before replacing recognized
official predecessor files. It changes neither the executable nor instructions,
settings, or Git data. A new release alone is not authorization.

With explicit authorization, install or remove the bundled AEC skill in one
existing harness home:

```text
aec codex install [--home ABSOLUTE_PATH]
aec codex uninstall [--home ABSOLUTE_PATH]
aec copilot install [--home ABSOLUTE_PATH]
aec copilot uninstall [--home ABSOLUTE_PATH]
```

These commands take no `--repo`. Install adds missing bundled files and refuses
conflicting copies. Uninstall removes only recognized official bundled files.
Both preserve runtime instructions, configuration, unrelated skills, and Git data.

To remove the executable and both skill enrollments, use the generated
uninstallation script with explicit authorization. On macOS:

```text
./scripts/uninstall-aec.sh [--codex-home ABSOLUTE_PATH]
```

It selects Copilot through `COPILOT_HOME` or the default home. It uses the
verified executable to remove the two bundled skills before removing the binary
and helper. Other installations, runtime instructions, settings, and Git data
remain intact.

## Initialize or attach

Ordinary Codex initialization:

```text
aec codex init --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]
```

For a missing/empty repository, require runtime `AGENTS.md`. Init warns
before enrolling `none` if runtime `config.toml` or root `personality` is
missing. It installs the skill, commits exact runtime instructions and managed
personality as `Backup Codex environment`, commits the reconciled AEC block as
`Initialize AEC instructions`, then applies both committed files. Runtime is
untouched until both commits succeed. Recognized current two-file or legacy
AGENTS-only baselines resume without rewriting the root commit; legacy resume
adds canonical config in the second commit. Ordinary init authorizes this
fixed lifecycle; do not wrap it in separate `backup` or `apply` calls.

For a completed pulled repository with the same recorded path, init installs
the skill and applies committed `AGENTS.md` and personality, even if either
runtime file is missing. It neither backs up runtime nor commits. A legacy
repository lacking committed canonical config stops; do not invent a value.

If the committed Codex `AGENTS.md` records a different repository path, show
both paths and ask for explicit confirmation before any mutation. Only then run:

```text
aec codex init --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH] --force-path-change
```

This installs the skill and binds only the local runtime AEC block to the
selected repository path while applying the committed Codex instructions and
personality. It leaves canonical bytes and Git `HEAD` unchanged. A later init
on the already-bound checkout needs no flag and leaves runtime unchanged;
use `aec codex status` and `aec codex apply` for drift. Never use the flag for a
fresh/partial repository, ChatGPT provider init, or `apply`.
For implicit homes, init stops if the other harness is bound to a different
checkout; the flag cannot override that conflict. Explicit custom-home flags
remain available but are outside this cross-harness check.

No `init` form pushes Git commits.

Manual ChatGPT backup scaffold:

```text
aec chatgpt init --repo ABSOLUTE_PATH
```

Do not pass `--home`. Resolve a recorded-path mismatch through the
confirmed ordinary-init flow first.

Local Copilot CLI initialization:

```text
aec copilot init --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH] [--force-path-change]
```

Copilot home resolves from `--home`, absolute `COPILOT_HOME`, then
`~/.copilot`. This alpha command reconciles native and canonical
`copilot-instructions.md` plus Copilot's AEC skill; it does not manage
`config.json`, stage, commit, push, or invoke Copilot. Use the `aec copilot`
group for Copilot lifecycle commands.
For a pulled checkout whose committed Copilot source records another machine's
path, ask before using `--force-path-change`; confirmed init changes only the
local runtime binding, not Git history. A conflicting implicit Codex checkout
still stops.

## Back up runtime to Git

Run `aec codex backup --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]` only with
approval for the repository write and commit. It attributes at most one changed
instruction section to its authored source, regenerates affected enrolled
targets, and/or captures a changed supported root personality. It makes one
exact-path commit for the changes. Missing runtime personality warns and stops;
stale target digests, ambiguous instruction edits, and wrong repository binding
also stop. Review those cases rather than guessing a source. Backup never
writes runtime, captures unrelated TOML, or pushes.

## Apply Git to runtime

Run `aec codex apply --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]` only with
approval to replace runtime. It deploys the enrolled committed target into an
existing, correctly bound runtime `AGENTS.md`; use ordinary init if that file
is missing. It adds/updates only the committed root personality (creating
`config.toml` if missing), preserves unrelated runtime TOML, and warns before
adding a missing managed value. It never captures runtime, changes Git,
commits, or pushes. On a committed-path mismatch, stop and suggest ordinary
`aec codex init`.
