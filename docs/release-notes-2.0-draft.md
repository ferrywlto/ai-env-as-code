# AEC 2.0 release notes — draft

The development executable remains `2.0.0-alpha.4`. The official 2.0 release
is deferred until Ferry verifies migration and requests the release.

## Changes

- Author shared instructions, platform policy and provider overlays separately.
  Explicit enrollment creates a current-platform Codex or Copilot target.
- Render enrolled targets from authored sources. Apply deploys committed targets,
  backup captures attributable runtime changes, and status compares local state.
- Manage the Codex root `personality` value while preserving unrelated TOML.
  Copilot instruction management preserves its unrelated configuration.
- Migrate a supported 1.x data repository with
  `aec migrate 2.0 --repo ABSOLUTE_PATH`. The interactive flow prepares
  sections, waits for exact `DONE`, commits targets, applies enrolled providers
  and records data format `2.0` after status verification.
- Continue to use the existing command and provider flag forms. The breaking
  provider-first CLI is deferred to [ticket #3](https://github.com/ferrywlto/ai-env-as-code/issues/3).
- Installers generate uninstall helpers with path and executable checks. Run
  the installer again to refresh an existing generated helper.

## Migration preparation

Close affected harness sessions and pause other writers until migration finishes.
Run migration from a separate terminal, review the prepared sections in a plain
editor, and enter `DONE` after review. Migration requires a tracked default-branch
remote and may fetch and fast-forward. It does not push.

If migration stops, inspect retained repository, index and runtime state before
starting a fresh invocation. Review the
[migration reference](reference.md#interactive-migration-command) and rehearse
on disposable copies before live use.

Real Codex and GitHub Copilot harness verification is deferred to
[ticket #4](https://github.com/ferrywlto/ai-env-as-code/issues/4). The
2.0 migration rehearsals and native runner checks do not prove real harness
behavior. The Copilot migration rehearsal used synthetic instructions.

The executable version and data-format marker `2.0` are separate
values. Successful migration commits the marker after verification. A late
failure can leave an uncommitted marker for inspection.
