---
name: aec
description: Initialize, inspect, back up, or apply the local GitHub Copilot CLI integration for an AI Environment as Code repository.
---

# AEC for GitHub Copilot

Use this skill only when the user explicitly requests initialization, status,
backup, or apply of their local GitHub Copilot CLI instructions through AEC.

The user must select the AEC data repository explicitly. Never infer it from the
current directory, the executable, or the skill location.

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

## Shared instructions (2.0 development)

Help the user classify each rule by scope: all enrolled harnesses belong in shared
instructions, local paths or OS rules in platform policy, and one harness's
mechanics in its provider overlay. Never infer this split from an existing
`copilot-instructions.md`; show a proposed split for review first.

After the user has reviewed and committed all three authored source files,
explicitly enroll Copilot on the current platform only when authorized:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot --enroll-shared [--copilot-home ABSOLUTE_PATH]
```

This creates only the portable Copilot target. It does not edit runtime,
commit, push, or replace the legacy init flow. Review the target before a
separate commit. For later approved source changes, show affected targets, then
run `aec render --repo ABSOLUTE_PATH` to regenerate the current platform's
enrolled canonical targets; review the source and target changes together and
obtain explicit approval before committing them.
`render` changes no runtime and creates no commit. The current `apply` and
`backup` commands still use their 1.x canonical paths, so do not claim a rendered
target was deployed. If v2 target enrollment is absent, stop this workflow and
explain that cross-harness deployment is not available yet.

For ordinary Copilot initialization, run:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

`--copilot-home` selects the local Copilot home. When omitted, AEC uses an absolute
`COPILOT_HOME` value and then `~/.copilot`. The command creates or updates only the
managed block in `copilot-instructions.md`, its canonical copy under the selected
repository, and this exact skill. It does not manage Copilot `config.json`.

For a read-only exact-byte comparison, run:

```text
aec status --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

It reports `in_sync`, `different`, or `missing`. Exit code 0 means in sync, 2
means drift or a missing runtime file, and 1 means validation failed. It never
changes runtime or canonical files.

For an explicitly authorized runtime-to-repository capture, run:

```text
aec backup --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

It validates the managed block and repository binding, replaces only the canonical
Copilot instruction file when necessary, and creates the fixed Git commit `Backup
Copilot instructions` when that canonical path differs from `HEAD`. It writes
`unchanged` when the canonical path is already committed with the runtime bytes.
It never changes runtime instructions or pushes the commit.

For an explicitly authorized committed repository-to-runtime deployment, run:

```text
aec apply --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

It requires the canonical file to match its committed Git blob and repository
binding, then atomically replaces only runtime `copilot-instructions.md`. It writes
`unchanged` when runtime already matches. It never captures runtime changes,
creates a commit, pushes, manages `config.json`, or installs/upgrades the skill.

Copilot support is alpha. `uninstall` and standalone skill upgrade are not yet
available for Copilot. Do not substitute Codex commands or flags, and do not claim
real-harness verification until Copilot CLI is installed and exercised on the
target machine.
