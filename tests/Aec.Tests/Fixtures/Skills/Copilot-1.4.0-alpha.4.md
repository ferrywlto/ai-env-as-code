---
name: aec
description: Initialize or inspect the local GitHub Copilot CLI integration for an AI Environment as Code repository.
---

# AEC for GitHub Copilot

Use this skill only when the user explicitly requests initialization or status
inspection of their local GitHub Copilot CLI instructions through AEC.

The user must select the AEC data repository explicitly. Never infer it from the
current directory, the executable, or the skill location. Run:

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

Copilot support is alpha. `backup`, `apply`, `uninstall`, and standalone skill
upgrade are not yet available for Copilot. Do not substitute Codex commands or
flags, and do not claim real-harness verification until Copilot CLI is installed
and exercised on the target machine.
