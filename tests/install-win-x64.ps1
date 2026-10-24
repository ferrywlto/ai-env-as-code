# Exercise the Windows installer from a disposable source copy. The real
# checkout's generated uninstaller and the user's Codex home are untouched.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
# Expected status drift returns 2. Check native exit codes explicitly rather
# than letting a caller's PowerShell preference turn that result into a throw.
$PSNativeCommandUseErrorActionPreference = $false

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "FAIL: $Message"
    }
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    try {
        & $Action | Out-Null
    } catch {
        return
    }
    throw "FAIL: $Message"
}

function Assert-Output([object[]]$Actual, [string[]]$Expected, [string]$Message) {
    Assert ($Actual.Count -eq $Expected.Count) $Message
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert ($Actual[$index] -ceq $Expected[$index]) $Message
    }
}

function Get-TestHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# Capture the exit code in the function that invokes the process: a helper's
# $LASTEXITCODE is not a reliable caller-side result. Only stdout is returned.
function Invoke-TestGit {
    $output = @(& git -C $dataRepo @args)
    $exitCode = $LASTEXITCODE
    Assert ($exitCode -eq 0) "fixture Git operation failed: $args"
    return $output
}

function Invoke-Provider(
    [string]$Provider, [string]$Command,
    [string[]]$Options = @(), [int]$ExpectedExitCode = 0
) {
    $homeOptions = switch ($Provider) {
        'codex' { @('--codex-home', $codexHome) }
        'copilot' { @('--provider=copilot', '--copilot-home', $copilotHome) }
        default { throw "Unknown fixture provider: $Provider" }
    }
    $output = @(& $customTarget $Command --repo $dataRepo @homeOptions @Options)
    $exitCode = $LASTEXITCODE
    Assert ($exitCode -eq $ExpectedExitCode) "$Provider $Command returned $exitCode instead of $ExpectedExitCode"
    return $output
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceInstaller = Join-Path $repoRoot 'scripts/install-win-x64.ps1'
$sourceArtifact = Join-Path $repoRoot 'artifacts/aec-win-x64/aec.exe'
Assert (Test-Path -LiteralPath $sourceInstaller -PathType Leaf) 'installer is missing'
Assert (Test-Path -LiteralPath $sourceArtifact -PathType Leaf) 'Native AOT artifact is missing'

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aec-win-installer-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$originalLocalAppData = $env:LOCALAPPDATA
$gitIdentityNames = @('GIT_AUTHOR_NAME', 'GIT_AUTHOR_EMAIL', 'GIT_COMMITTER_NAME', 'GIT_COMMITTER_EMAIL',
    'GIT_CONFIG_GLOBAL', 'GIT_CONFIG_NOSYSTEM')
$originalGitIdentity = @{}
foreach ($name in $gitIdentityNames) {
    $originalGitIdentity[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $sourceRoot = Join-Path $testRoot 'source'
    $scriptsDir = Join-Path $sourceRoot 'scripts'
    $artifactDir = Join-Path $sourceRoot 'artifacts/aec-win-x64'
    $null = New-Item -ItemType Directory -Path $scriptsDir -Force
    $null = New-Item -ItemType Directory -Path $artifactDir -Force
    $installer = Join-Path $scriptsDir 'install-win-x64.ps1'
    $artifact = Join-Path $artifactDir 'aec.exe'
    $uninstaller = Join-Path $scriptsDir 'uninstall-aec-win-x64.ps1'
    Copy-Item -LiteralPath $sourceInstaller -Destination $installer
    Copy-Item -LiteralPath $sourceArtifact -Destination $artifact
    $expectedHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    Assert-Throws { & $installer -InstallDir 'relative' } 'relative install directory was accepted'
    Assert-Throws { & $installer -InstallDir 'C:relative' } 'drive-relative install directory was accepted'
    Assert-Throws { & $installer -InstallDir '\\server\share' } 'UNC install directory was accepted'

    # Redirect only this test process's default app-data path into the temporary
    # root; the user's registry and persistent environment are not changed.
    $env:LOCALAPPDATA = Join-Path $testRoot 'home/AppData/Local'
    $defaultTarget = Join-Path $env:LOCALAPPDATA 'Programs/AEC/aec.exe'
    $initial = @(& $installer)
    Assert (Test-Path -LiteralPath $defaultTarget -PathType Leaf) 'default executable was not installed'
    Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'uninstaller was not generated'
    Assert ((Get-FileHash -LiteralPath $defaultTarget -Algorithm SHA256).Hash -ceq $expectedHash) 'installed bytes differ'
    Assert ($initial -contains "installed $defaultTarget") 'default install did not report the binary'
    Assert ($initial -contains "installed $uninstaller") 'default install did not report the helper'

    $again = @(& $installer)
    Assert ($again -contains "unchanged $defaultTarget") 'reinstall was not idempotent'

    $customDir = Join-Path $testRoot "custom O'Connor/bin"
    $customTarget = Join-Path $customDir 'aec.exe'
    $customOutput = @(& $installer -InstallDir $customDir)
    Assert ($customOutput -contains "installed $customTarget") 'custom install failed'
    Assert (Test-Path -LiteralPath $defaultTarget -PathType Leaf) 'custom install removed old binary'
    Assert ((Get-FileHash -LiteralPath $customTarget -Algorithm SHA256).Hash -ceq $expectedHash) 'custom installed bytes differ'

    # A generated helper must refuse a changed binary, invalid Codex home, or
    # failed AEC cleanup without deleting either installation file.
    $codexHome = Join-Path $testRoot 'codex-home'
    $null = New-Item -ItemType Directory -Path $codexHome
    $config = Join-Path $codexHome 'config.toml'
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($config,
        "model = `"keep-local-model`"`npersonality = `"friendly`"`n[features]`nlocal_setting = true`n", $utf8NoBom)
    $configHash = Get-TestHash $config
    $copiedHelper = Join-Path $testRoot 'copied-uninstaller.ps1'
    Copy-Item -LiteralPath $uninstaller -Destination $copiedHelper
    Assert-Throws { & $copiedHelper -CodexHome $codexHome } 'copied helper was accepted'
    Assert (Test-Path -LiteralPath $customTarget -PathType Leaf) 'copied helper removed binary'
    Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'copied helper removed original helper'
    Assert-Throws { & $uninstaller -CodexHome relative } 'relative Codex home was accepted'
    Assert (Test-Path -LiteralPath $customTarget -PathType Leaf) 'invalid argument removed binary'
    [System.IO.File]::AppendAllText($customTarget, 'tamper')
    Assert-Throws { & $uninstaller -CodexHome (Join-Path $testRoot 'missing-codex-home') } 'modified binary was accepted'
    Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'hash failure removed helper'
    $null = & $installer -InstallDir $customDir
    Assert ((Get-FileHash -LiteralPath $customTarget -Algorithm SHA256).Hash -ceq $expectedHash) 'reinstall did not repair binary'
    Assert-Throws { & $uninstaller -CodexHome (Join-Path $testRoot 'missing-codex-home') } 'failed runtime cleanup was accepted'
    Assert (Test-Path -LiteralPath $customTarget -PathType Leaf) 'failed runtime cleanup removed binary'
    Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'failed runtime cleanup removed helper'

    # Initialize real managed runtime state in this disposable Codex home. A
    # successful uninstall must remove its AEC block and skill, not merely
    # return unchanged for an empty home.
    $runtimeAgents = Join-Path $codexHome 'AGENTS.md'
    [System.IO.File]::WriteAllText($runtimeAgents, "Shared approval.`nLocal paths.`nCodex models.`n", $utf8NoBom)
    $dataRepo = Join-Path $testRoot 'aec-data'
    # AEC init commits the disposable repository; CI runners have no Git identity.
    # These process-only values leave the user's Git configuration untouched.
    [Environment]::SetEnvironmentVariable('GIT_AUTHOR_NAME', 'AEC Installer Test', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_AUTHOR_EMAIL', 'aec-installer-test@example.invalid', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_COMMITTER_NAME', 'AEC Installer Test', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_COMMITTER_EMAIL', 'aec-installer-test@example.invalid', 'Process')
    # Disable signing and newline conversion in this process only. Authored
    # sources and rendered targets have exact LF/no-BOM byte contracts.
    $env:GIT_CONFIG_GLOBAL = Join-Path $testRoot 'gitconfig'
    $env:GIT_CONFIG_NOSYSTEM = '1'
    [System.IO.File]::WriteAllText($env:GIT_CONFIG_GLOBAL,
        "[commit]`n    gpgSign = false`n[core]`n    autocrlf = false`n    safecrlf = false`n", $utf8NoBom)
    & $customTarget init --repo $dataRepo --codex-home $codexHome | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'isolated AEC initialization failed'
    $skill = Join-Path $codexHome 'skills/aec/SKILL.md'
    Assert (Test-Path -LiteralPath $skill -PathType Leaf) 'AEC skill was not installed'
    Assert ([System.IO.File]::ReadAllText($runtimeAgents).Contains('<!-- AEC:BEGIN')) 'managed block was not installed'
    $canonicalAgents = Join-Path $dataRepo 'environment/providers/codex/AGENTS.md'
    $canonicalHash = (Get-FileHash -LiteralPath $canonicalAgents -Algorithm SHA256).Hash

    # Exercise the same Native AOT executable against Windows-style Copilot paths.
    # This validates AEC's local files without requiring Copilot CLI on the runner.
    $copilotHome = Join-Path $testRoot 'copilot-home'
    $null = New-Item -ItemType Directory -Path $copilotHome
    $runtimeCopilotInstructions = Join-Path $copilotHome 'copilot-instructions.md'
    [System.IO.File]::WriteAllText($runtimeCopilotInstructions, "Shared approval.`nLocal paths.`nCopilot tools.`n", $utf8NoBom)
    $copilotConfig = Join-Path $copilotHome 'config.json'
    [System.IO.File]::WriteAllText($copilotConfig, "{`"localSetting`":true}`n", $utf8NoBom)
    $copilotConfigHash = Get-TestHash $copilotConfig
    & $customTarget init --repo $dataRepo --provider=copilot --copilot-home $copilotHome | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'isolated Copilot initialization failed'
    $canonicalCopilotInstructions = Join-Path $dataRepo 'environment/providers/copilot/copilot-instructions.md'
    $copilotSkill = Join-Path $copilotHome 'skills/aec/SKILL.md'
    Assert (Test-Path -LiteralPath $canonicalCopilotInstructions -PathType Leaf) 'canonical Copilot instructions were not created'
    Assert (Test-Path -LiteralPath $copilotSkill -PathType Leaf) 'Copilot AEC skill was not installed'
    Assert ((Get-FileHash -LiteralPath $runtimeCopilotInstructions -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $canonicalCopilotInstructions -Algorithm SHA256).Hash) 'Copilot runtime and canonical instructions differ'
    Assert ([System.IO.File]::ReadAllText($runtimeCopilotInstructions).Contains('<!-- AEC:COPILOT:BEGIN')) 'Copilot managed block was not installed'
    $copilotCanonicalHash = Get-TestHash $canonicalCopilotInstructions

    # Legacy init does not enroll portable targets. Commit its Copilot baseline,
    # then author an explicit split before using the public 2.0 commands.
    Invoke-TestGit add -- 'environment/providers/copilot/copilot-instructions.md' | Out-Null
    Invoke-TestGit commit -m 'Record smoke Copilot baseline' | Out-Null
    $platform = 'windows-x64'
    $sharedSource = Join-Path $dataRepo 'environment/shared/instructions.md'
    $platformSource = Join-Path $dataRepo "environment/platforms/$platform/policy.md"
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $sharedSource) -Force
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $platformSource) -Force
    [System.IO.File]::WriteAllText($sharedSource, "Shared approval.`n", $utf8NoBom)
    [System.IO.File]::WriteAllText($platformSource, "Local paths.`n", $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $dataRepo 'environment/providers/codex/overlay.md'), "Codex models.`n", $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $dataRepo 'environment/providers/copilot/overlay.md'), "Copilot tools.`n", $utf8NoBom)
    Invoke-TestGit add -- 'environment/shared/instructions.md' "environment/platforms/$platform/policy.md" `
        'environment/providers/codex/overlay.md' 'environment/providers/copilot/overlay.md' | Out-Null
    Invoke-TestGit commit -m 'Author smoke instruction sources' | Out-Null

    $providers = @(
        @{ Name = 'codex'; File = 'AGENTS.md'; Runtime = $runtimeAgents;
           Before = 'Codex models.'; After = 'Codex models updated.'; Subject = 'Backup portable Codex environment' },
        @{ Name = 'copilot'; File = 'copilot-instructions.md'; Runtime = $runtimeCopilotInstructions;
           Before = 'Copilot tools.'; After = 'Copilot tools updated.'; Subject = 'Backup portable instructions' }
    )
    $legacyRuntimeHash = Get-TestHash $runtimeAgents
    $legacyCopilotRuntimeHash = Get-TestHash $runtimeCopilotInstructions
    foreach ($provider in $providers) {
        $name = $provider.Name
        $targetRelative = "environment/targets/$platform/$name/$($provider.File)"
        $headBefore = Invoke-TestGit rev-parse HEAD
        $enroll = @(Invoke-Provider $name 'init' -Options @('--enroll-shared'))
        Assert-Output $enroll @("enrolled $targetRelative") 'unexpected enrollment output'
        Assert ((Invoke-TestGit rev-parse HEAD) -ceq $headBefore) 'enrollment committed'
        Invoke-TestGit diff --cached --quiet | Out-Null
        Assert ((Get-TestHash $runtimeAgents) -ceq $legacyRuntimeHash) 'enrollment changed Codex runtime'
        Assert ((Get-TestHash $runtimeCopilotInstructions) -ceq $legacyCopilotRuntimeHash) 'enrollment changed Copilot runtime'
        if ($name -eq 'codex') {
            Assert (-not (Test-Path -LiteralPath (Join-Path $dataRepo "environment/targets/$platform/copilot"))) 'enrollment invented a Copilot target'
        }
        Invoke-TestGit add -- $targetRelative | Out-Null
        Invoke-TestGit commit -m "Enroll smoke $name target" | Out-Null
    }

    foreach ($provider in $providers) {
        $name = $provider.Name
        $targetRelative = "environment/targets/$platform/$name/$($provider.File)"
        $overlayRelative = "environment/providers/$name/overlay.md"
        $expectedStatus = @("$name/$($provider.File) in_sync")
        $expectedDrift = @("$name/$($provider.File) different")
        if ($name -eq 'codex') {
            $expectedStatus = @('codex/AGENTS.md   in_sync', 'codex/config.toml in_sync')
            $expectedDrift = @('codex/AGENTS.md   different', 'codex/config.toml in_sync')
        }
        $headBefore = Invoke-TestGit rev-parse HEAD
        Assert-Output @(Invoke-Provider $name 'apply') @('applied') 'initial apply output differs'
        Assert ((Invoke-TestGit rev-parse HEAD) -ceq $headBefore) 'initial apply changed HEAD'
        Assert-Output @(Invoke-Provider $name 'status') $expectedStatus 'initial status differs'

        # Edit one provider section without adding outside text or a final newline.
        $runtime = [System.IO.File]::ReadAllText($provider.Runtime)
        $edited = $runtime.Replace($provider.Before, $provider.After, [StringComparison]::Ordinal)
        Assert ($edited -cne $runtime) 'fixture did not introduce provider drift'
        [System.IO.File]::WriteAllText($provider.Runtime, $edited, $utf8NoBom)
        $runtimeHashBeforeBackup = Get-TestHash $provider.Runtime
        $headBefore = Invoke-TestGit rev-parse HEAD
        $backup = @(Invoke-Provider $name 'backup')
        $backupHead = Invoke-TestGit rev-parse HEAD
        Assert-Output $backup @("committed $backupHead") 'backup output differs'
        Assert ((Invoke-TestGit rev-parse 'HEAD^') -ceq $headBefore) 'backup did not create one commit'
        Assert ((Invoke-TestGit log -1 '--format=%s') -ceq $provider.Subject) 'backup subject differs'
        Assert-Output @(Invoke-TestGit diff-tree --no-commit-id --name-only -r HEAD) `
            @($overlayRelative, $targetRelative) 'backup committed unexpected paths'
        $expectedOverlay = Join-Path $testRoot 'expected-overlay'
        [System.IO.File]::WriteAllText($expectedOverlay, $provider.After + "`n", $utf8NoBom)
        Assert ((Get-TestHash (Join-Path $dataRepo $overlayRelative)) -ceq (Get-TestHash $expectedOverlay)) 'backup source bytes differ'
        Assert ((Get-TestHash $provider.Runtime) -ceq $runtimeHashBeforeBackup) 'backup rewrote runtime'

        # Backup preserves the last-applied digest; apply refreshes it from the
        # new committed target. That intermediate status must report exit 2.
        Assert-Output @(Invoke-Provider $name 'status' -ExpectedExitCode 2) $expectedDrift 'post-backup drift differs'
        Assert-Output @(Invoke-Provider $name 'apply') @('applied') 'digest refresh output differs'
        Assert-Output @(Invoke-Provider $name 'status') $expectedStatus 'refreshed status differs'
        $appliedHash = Get-TestHash $provider.Runtime
        [System.IO.File]::AppendAllText($provider.Runtime, "`nWindows apply drift`n", $utf8NoBom)
        Assert-Output @(Invoke-Provider $name 'apply') @('applied') 'drift apply output differs'
        Assert ((Get-TestHash $provider.Runtime) -ceq $appliedHash) 'apply did not restore committed portable bytes'
        Assert-Output @(Invoke-Provider $name 'status') $expectedStatus 'final status differs'
        Assert-Output @(Invoke-Provider $name 'backup') @('unchanged') 'repeat backup differs'
        Assert-Output @(Invoke-Provider $name 'apply') @('unchanged') 'repeat apply differs'
        Assert ((Invoke-TestGit rev-parse HEAD) -ceq $backupHead) 'apply or idempotent commands changed HEAD'
    }
    Assert ((Get-TestHash $canonicalAgents) -ceq $canonicalHash) 'portable lifecycle changed legacy Codex source'
    Assert ((Get-TestHash $canonicalCopilotInstructions) -ceq $copilotCanonicalHash) 'portable lifecycle changed legacy Copilot source'
    Assert ((Get-TestHash $config) -ceq $configHash) 'portable lifecycle changed Codex config'
    Assert ((Get-TestHash $copilotConfig) -ceq $copilotConfigHash) 'portable lifecycle changed Copilot config'
    Assert (@(Invoke-TestGit status --porcelain).Count -eq 0) 'portable lifecycle left repository changes'

    # A shared edit renders both enrolled targets without touching runtime or Git.
    $codexAuthoredBeforeRender = Join-Path $testRoot 'codex-authored-before-render'
    Copy-Item -LiteralPath (Join-Path $dataRepo "environment/targets/$platform/codex/AGENTS.md") -Destination $codexAuthoredBeforeRender
    $headBeforeRender = Invoke-TestGit rev-parse HEAD
    $codexRuntimeHash = (Get-FileHash -LiteralPath $runtimeAgents -Algorithm SHA256).Hash
    $configRuntimeHash = (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
    $copilotRuntimeHash = (Get-FileHash -LiteralPath $runtimeCopilotInstructions -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText($sharedSource, "Shared approval updated.`n", $utf8NoBom)
    $renderOutput = @(& $customTarget render --repo $dataRepo)
    Assert ($LASTEXITCODE -eq 0) 'Native AOT render failed'
    Assert-Output $renderOutput @("rendered environment/targets/$platform/codex/AGENTS.md",
        "rendered environment/targets/$platform/copilot/copilot-instructions.md") 'unexpected render output'
    foreach ($provider in $providers) {
        $expectedRendered = @(
            '<!-- AEC:SOURCE:SHARED:BEGIN -->', 'Shared approval updated.', '',
            '<!-- AEC:SOURCE:SHARED:END -->', '',
            '<!-- AEC:SOURCE:PLATFORM:BEGIN -->', 'Local paths.', '',
            '<!-- AEC:SOURCE:PLATFORM:END -->', '',
            '<!-- AEC:SOURCE:PROVIDER:BEGIN -->', $provider.After, '',
            '<!-- AEC:SOURCE:PROVIDER:END -->'
        ) -join "`n"
        $expectedTarget = Join-Path $testRoot 'expected-rendered-target'
        [System.IO.File]::WriteAllText($expectedTarget, $expectedRendered, $utf8NoBom)
        $renderedTarget = Join-Path $dataRepo "environment/targets/$platform/$($provider.Name)/$($provider.File)"
        Assert ((Get-TestHash $renderedTarget) -ceq (Get-TestHash $expectedTarget)) 'rendered target bytes differ'
    }
    $renderAgainOutput = @(& $customTarget render --repo $dataRepo)
    Assert ($LASTEXITCODE -eq 0 -and $renderAgainOutput.Count -eq 1 -and $renderAgainOutput[0] -ceq 'unchanged') 'repeat render was not idempotent'
    Assert ((Invoke-TestGit rev-parse HEAD) -ceq $headBeforeRender) 'render changed repository HEAD'
    Invoke-TestGit diff --cached --quiet | Out-Null
    Assert ((Get-FileHash -LiteralPath $runtimeAgents -Algorithm SHA256).Hash -ceq $codexRuntimeHash) 'render changed Codex runtime instructions'
    Assert ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -ceq $configRuntimeHash) 'render changed Codex runtime config'
    Assert ((Get-FileHash -LiteralPath $runtimeCopilotInstructions -Algorithm SHA256).Hash -ceq $copilotRuntimeHash) 'render changed Copilot runtime instructions'
    Assert ((Get-TestHash $copilotConfig) -ceq $copilotConfigHash) 'render changed Copilot config'
    $renderedPaths = @('environment/shared/instructions.md',
        "environment/targets/$platform/codex/AGENTS.md", "environment/targets/$platform/copilot/copilot-instructions.md")
    Assert-Output @(Invoke-TestGit diff --name-only) $renderedPaths 'render changed unexpected repository paths'
    $enrolledPlatforms = @(Get-ChildItem -LiteralPath (Join-Path $dataRepo 'environment/targets') -Directory)
    Assert ($enrolledPlatforms.Count -eq 1 -and $enrolledPlatforms[0].Name -ceq $platform) 'render invented an unused platform'

    # Uninstall must preserve the entire data tree, including this deliberate
    # uncommitted render, and the other harness's runtime and skill.
    $dataHashes = @{}
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $dataRepo 'environment') -Recurse -File)) {
        $dataHashes[$file.FullName] = Get-TestHash $file.FullName
    }
    $copilotSkillHash = Get-TestHash $copilotSkill

    & $uninstaller -CodexHome $codexHome | Out-Null
    Assert (-not (Test-Path -LiteralPath $customTarget)) 'uninstall left custom binary'
    Assert (-not (Test-Path -LiteralPath $uninstaller)) 'uninstall left helper'
    Assert (Test-Path -LiteralPath $config -PathType Leaf) 'uninstall removed runtime config'
    Assert ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -ceq $configHash) 'uninstall changed runtime config'
    Assert ((Get-TestHash $runtimeAgents) -ceq (Get-TestHash $codexAuthoredBeforeRender)) 'uninstall changed applied authored instruction sections'
    Assert (-not ([System.IO.File]::ReadAllText($runtimeAgents).Contains('<!-- AEC:BEGIN'))) 'uninstall left managed block'
    Assert (-not (Test-Path -LiteralPath $skill)) 'uninstall left AEC skill'
    Assert ((Get-FileHash -LiteralPath $canonicalAgents -Algorithm SHA256).Hash -ceq $canonicalHash) 'uninstall changed data repository'
    $dataFilesAfter = @(Get-ChildItem -LiteralPath (Join-Path $dataRepo 'environment') -Recurse -File)
    Assert ($dataFilesAfter.Count -eq $dataHashes.Count) 'uninstall changed data file count'
    foreach ($file in $dataFilesAfter) {
        Assert ($dataHashes.ContainsKey($file.FullName) -and (Get-TestHash $file.FullName) -ceq $dataHashes[$file.FullName]) 'uninstall changed data bytes'
    }
    Assert ((Invoke-TestGit rev-parse HEAD) -ceq $headBeforeRender) 'uninstall changed data HEAD'
    Invoke-TestGit diff --cached --quiet | Out-Null
    Assert-Output @(Invoke-TestGit diff --name-only) $renderedPaths 'uninstall changed data diff'
    Assert ((Get-TestHash $runtimeCopilotInstructions) -ceq $copilotRuntimeHash) 'uninstall changed Copilot runtime'
    Assert ((Get-TestHash $copilotConfig) -ceq $copilotConfigHash) 'uninstall changed Copilot config'
    Assert ((Get-TestHash $copilotSkill) -ceq $copilotSkillHash) 'uninstall changed Copilot skill'
    Assert (Test-Path -LiteralPath $defaultTarget -PathType Leaf) 'uninstall removed older install target'

    # A personal script at the helper path must be preserved. This check uses
    # another isolated source so the successful install above cannot mask it.
    $conflictRoot = Join-Path $testRoot 'conflict-source'
    $conflictScripts = Join-Path $conflictRoot 'scripts'
    $conflictArtifactDir = Join-Path $conflictRoot 'artifacts/aec-win-x64'
    $null = New-Item -ItemType Directory -Path $conflictScripts -Force
    $null = New-Item -ItemType Directory -Path $conflictArtifactDir -Force
    Copy-Item -LiteralPath $sourceInstaller -Destination (Join-Path $conflictScripts 'install-win-x64.ps1')
    Copy-Item -LiteralPath $sourceArtifact -Destination (Join-Path $conflictArtifactDir 'aec.exe')
    $personalHelper = Join-Path $conflictScripts 'uninstall-aec-win-x64.ps1'
    [System.IO.File]::WriteAllText($personalHelper, 'personal script')
    $conflictTarget = Join-Path $testRoot 'conflict/bin/aec.exe'
    Assert-Throws {
        & (Join-Path $conflictScripts 'install-win-x64.ps1') -InstallDir (Join-Path $testRoot 'conflict/bin')
    } 'personal uninstaller was overwritten'
    Assert (-not (Test-Path -LiteralPath $conflictTarget)) 'conflicting install wrote a binary'
    Assert ([System.IO.File]::ReadAllText($personalHelper) -ceq 'personal script') 'personal script changed'

    Write-Output 'Windows installer, AEC lifecycle, and render smoke passed'
} finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    foreach ($name in $gitIdentityNames) {
        [Environment]::SetEnvironmentVariable($name, $originalGitIdentity[$name], 'Process')
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
