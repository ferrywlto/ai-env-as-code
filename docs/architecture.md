# AEC architecture (2.0 development branch)

This is the **current implementation**, not a target-state design. The engine,
the selected `--repo` data repository, and each local harness runtime are
separate. Git commits in the data repository are the source of truth. The
class diagrams show the main relationships, not every implementation helper.

## Components

```mermaid
flowchart LR
    User[User] --> Skill[Installed AEC skill]
    User --> CLI[AEC CLI]
    Skill --> CLI
    Skill -.->|remote preflight and approved push| Remote[(Git remote)]

    subgraph Engine[AEC engine repository and installed binary]
        CLI --> Route[AecApplication]
        Route --> Compose[Enrollment and render]
        Route --> Guard[Implicit-home binding check]
        Guard --> Attach[Codex or Copilot init attachment]
        Route --> Inspect[Status and previews]
        Route --> Writers[Backup and apply writers]
    end

    subgraph Data[Selected Git data repository: --repo]
        Sources[Shared + platform + provider authored sources]
        Targets[Enrolled platform/provider targets]
        Config[Codex canonical config.toml]
        History[(Git commits)]
    end

    Compose -->|render; no commit| Targets
    Sources --> Compose
    Sources --> History
    Targets --> History
    Config --> History
    History --> Inspect
    History -->|clean committed provider source| Attach
    Attach -->|machine-local path binding; no path-only commit| Runtime[Local Codex or Copilot runtime]
    Runtime -->|read other implicit harness binding| Guard
    Inspect --> Writers
    Writers -->|backup: changed source| Sources
    Writers -->|backup: affected targets| Targets
    Writers -->|backup: managed config| Config
    Writers -->|commit exact paths| History
    Writers -->|apply: committed target/config| Runtime
    Runtime --> Inspect
```

`aec render` composes only already-enrolled targets for the current platform;
`aec init --enroll-shared` enrolls one selected provider. Neither commits nor
deploys. `status` reads, `backup` moves runtime changes into the repository and
commits, and `apply` moves committed repository content into runtime. The CLI
does not fetch or push; the installed skill coordinates remote checks and any
approved Git operations. Codex also manages root `personality` in
`config.toml`; Copilot currently manages only its instruction file.
Confirmed Codex or Copilot attachment of a clone at a new local path changes
only its runtime binding; it does not commit a machine-specific path to shared
history. Copilot requires a clean committed canonical source for this rebind.
Ordinary init checks the other harness's implicit home before writes and stops
on a different repository binding. Explicit custom homes are outside that
cross-harness check in this 2.0 alpha slice.

## Classes: source composition and runtime inspection

```mermaid
classDiagram
    class AecApplication {
        +Run(args, output, error)
    }
    class SharedEnrollmentCommand {
        +Run(repository, provider, providerHome, output)
    }
    class RenderCommand {
        +Run(repository, output)
        +CurrentPlatform()
    }
    class InstructionSections {
        +Render(shared, platform, provider)
        +Parse(content, hasPlatform, hasProvider)
    }
    class PortableRuntimeSnapshot {
        +Read(repository, provider, providerHome)
        +Commit
        +Target
        +Runtime
    }
    class CommittedTargetValidator {
        +Read(repository, platform, provider)
    }
    class PortableEnvironmentStatus {
        +Inspect(repository, provider, providerHome)
    }
    class PortableApplyPreview {
        +Create(repository, provider, providerHome)
        +RenderDesiredRuntime(snapshot)
    }
    class PortableCodexConfigPreview {
        +Create(snapshot)
    }

    AecApplication --> SharedEnrollmentCommand : init --enroll-shared
    AecApplication --> RenderCommand : render
    AecApplication --> PortableEnvironmentStatus : status
    AecApplication --> PortableApplyPreview : apply preflight
    SharedEnrollmentCommand --> InstructionSections : compose
    RenderCommand --> InstructionSections : compose
    PortableRuntimeSnapshot --> CommittedTargetValidator : read committed target
    PortableEnvironmentStatus --> PortableRuntimeSnapshot : inspect
    PortableEnvironmentStatus --> PortableCodexConfigPreview : Codex only
    PortableApplyPreview --> PortableRuntimeSnapshot : inspect
    PortableApplyPreview --> PortableCodexConfigPreview : Codex only
```

The snapshot binds a committed target to the current platform, selected data
repository, and local runtime control block. It is the common read boundary;
previews plan changes but do not authorize writes without revalidation.

## Classes: directional writes

```mermaid
classDiagram
    class AecApplication
    class PortableApplyPreview
    class PortableRuntimeSnapshot
    class PortableInstructionWriter {
        +Apply(reviewed)
    }
    class PortableCodexWriter {
        +Apply(reviewed)
    }
    class PortableCodexBackupPreview {
        +Create(repository, codexHome)
    }
    class PortableCopilotBackupPreview {
        +Create(repository, copilotHome)
    }
    class PortableBackupAttributionPreview {
        +Create(snapshot)
    }
    class PortableCodexBackupWriter {
        +WriteAndCommit(reviewed)
    }
    class PortableCopilotBackupWriter {
        +WriteAndCommit(reviewed)
    }
    class PortableBackupSourceWriter {
        +Write(snapshot, attribution)
    }
    class PortableBackupTargetWriter {
        +Render(snapshot, attribution)
    }
    class PortableBackupCommitter {
        +Commit(reviewed, renderedPaths)
        +Commit(snapshot, attribution, renderedPaths)
    }

    AecApplication --> PortableApplyPreview : Codex and Copilot apply
    AecApplication --> PortableCodexWriter : Codex apply
    AecApplication --> PortableInstructionWriter : Copilot apply
    PortableCodexWriter --> PortableInstructionWriter : instruction step
    AecApplication --> PortableCodexBackupPreview : Codex backup preflight
    PortableCodexBackupPreview --> PortableBackupAttributionPreview : attribute one section
    PortableCopilotBackupPreview --> PortableRuntimeSnapshot : read committed target and binding
    PortableCopilotBackupPreview --> PortableBackupAttributionPreview : attribute one section
    AecApplication --> PortableCodexBackupWriter : Codex backup
    PortableCodexBackupWriter --> PortableBackupSourceWriter : changed authored source
    PortableCodexBackupWriter --> PortableBackupTargetWriter : affected enrolled targets
    PortableCodexBackupWriter --> PortableBackupCommitter : exact-path commit
    AecApplication --> PortableCopilotBackupPreview : Copilot backup preflight
    AecApplication --> PortableCopilotBackupWriter : Copilot backup
    PortableCopilotBackupWriter --> PortableCopilotBackupPreview : recheck inputs
    PortableCopilotBackupWriter --> PortableBackupSourceWriter : one authored source
    PortableCopilotBackupWriter --> PortableBackupTargetWriter : affected enrolled targets
    PortableCopilotBackupWriter --> PortableBackupCommitter : exact-path commit
```

The Codex backup writer rechecks runtime inputs and Git state before replacing
canonical files, then commits only the affected paths. An interrupted
multi-file write can leave partial uncommitted changes, so it reports the need
to inspect rather than rolling back automatically. Copilot `status` and
`apply` use the portable target. Public Copilot `backup` now uses the same
snapshot and one-section attribution rules. Its writer rechecks the preview,
refuses pre-existing staged changes, and commits one attributed source plus
affected targets. The retained 1.x Copilot backup helper is not routed from
the public command.
