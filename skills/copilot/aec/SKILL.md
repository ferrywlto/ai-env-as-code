---
name: aec
description: Use for AEC-managed local GitHub Copilot CLI instructions, including init, status, backup, apply, enrollment, and render.
---

# AEC for GitHub Copilot

Require an explicitly selected absolute AEC data-repository path for `--repo`;
never infer it from the engine checkout, current directory, executable, or
skill. Use only for local Copilot CLI instruction work through AEC.

## Git remote preflight

Only before an AEC canonical write (`init`, `backup`, `render`, or a manual edit)
or runtime `apply`, inspect Git state and use `aec status` for an existing
enrolled provider to identify local drift and confirm direction. Skip status
for a new repository without a baseline. Do not run preflight on unrelated
replies. `aec status` never checks the remote.

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
but warn it is not shared. Never automatically pull, merge, rebase, or
force-push.

After a canonical commit, obtain separate push approval. Resolve and show the
push destination; ask if it differs from the fetch remote or is ambiguous.
Push only the current branch to an explicit destination and verify publication
before routine remote-backed `apply`; stop if push fails. `backup`, `render`,
and `init` do not themselves authorize pushing.

## Shared instructions (2.0 development)

Propose the source of each rule for review: shared across enrolled harnesses,
platform-specific paths/OS policy, or Copilot-only overlay. Never infer a
split from existing `copilot-instructions.md`.

After all three authored sources are reviewed and committed, enroll Copilot
on the current platform only with authorization:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot --enroll-shared [--copilot-home ABSOLUTE_PATH]
```

Enrollment creates only the portable target: no runtime write, commit, or
push. Review it before a separate commit. For later approved source changes,
show affected targets, run `aec render --repo ABSOLUTE_PATH`, then review and
separately approve the source/target commit. `render` does not touch runtime
or Git history. Current `backup`/`apply` still use 1.x canonical files; do
not claim a rendered target was deployed. Stop if v2 enrollment is absent.

Ordinary Copilot initialization:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

Copilot home resolves from `--copilot-home`, absolute `COPILOT_HOME`, then
`~/.copilot`. Init reconciles only native/canonical
`copilot-instructions.md` and this skill, not `config.json`.

Read-only exact-byte comparison:

```text
aec status --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

`in_sync` exits 0; `different`/`missing` exit 2; validation failure exits 1.
Status never changes runtime or canonical files.

Only with approval for runtime-to-repository capture and commit:

```text
aec backup --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

Backup validates the block and binding, changes only the canonical Copilot
instructions, and commits changed bytes as `Backup Copilot instructions`.
If already committed with runtime bytes, it reports `unchanged`. It never
writes runtime or pushes.

Only with approval for committed repository-to-runtime deployment:

```text
aec apply --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

Apply requires a committed canonical file and matching repository binding,
then atomically replaces only runtime `copilot-instructions.md`; matching
runtime reports `unchanged`. It never captures runtime, commits, pushes,
manages `config.json`, or installs/upgrades the skill.

Copilot support is alpha; uninstall and skill upgrade are unavailable. Do
not substitute Codex flags or claim real-harness verification without testing
the installed Copilot CLI on the target machine.
