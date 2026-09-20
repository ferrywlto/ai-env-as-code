---
name: aec
description: Initialize the local GitHub Copilot CLI integration for an AI Environment as Code repository.
---

# AEC for GitHub Copilot

Use this skill only when the user explicitly requests initialization of their local
GitHub Copilot CLI instructions through AEC.

The user must select the AEC data repository explicitly. Never infer it from the
current directory, the executable, or the skill location. Run:

```text
aec init --repo ABSOLUTE_PATH --provider=copilot [--copilot-home ABSOLUTE_PATH]
```

`--copilot-home` selects the local Copilot home. When omitted, AEC uses an absolute
`COPILOT_HOME` value and then `~/.copilot`. The command creates or updates only the
managed block in `copilot-instructions.md`, its canonical copy under the selected
repository, and this exact skill. It does not manage Copilot `config.json`.

Copilot support is alpha. `status`, `backup`, `apply`, `uninstall`, and skill
upgrade are not yet available for Copilot. Do not substitute Codex commands or
flags, and do not claim real-harness verification until Copilot CLI is installed
and exercised on the target machine.
