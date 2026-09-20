# Exercise the Windows installer from a disposable source copy. The real
# checkout's generated uninstaller and the user's Codex home are untouched.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

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

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceInstaller = Join-Path $repoRoot 'scripts/install-win-x64.ps1'
$sourceArtifact = Join-Path $repoRoot 'artifacts/aec-win-x64/aec.exe'
Assert (Test-Path -LiteralPath $sourceInstaller -PathType Leaf) 'installer is missing'
Assert (Test-Path -LiteralPath $sourceArtifact -PathType Leaf) 'Native AOT artifact is missing'

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aec-win-installer-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$originalLocalAppData = $env:LOCALAPPDATA
$gitIdentityNames = @('GIT_AUTHOR_NAME', 'GIT_AUTHOR_EMAIL', 'GIT_COMMITTER_NAME', 'GIT_COMMITTER_EMAIL')
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
    [System.IO.File]::WriteAllText($config, 'personality = "none"')
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
    [System.IO.File]::WriteAllText($runtimeAgents, 'Personal instructions' + [Environment]::NewLine)
    $dataRepo = Join-Path $testRoot 'aec-data'
    # AEC init commits the disposable repository; CI runners have no Git identity.
    # These process-only values leave the user's Git configuration untouched.
    [Environment]::SetEnvironmentVariable('GIT_AUTHOR_NAME', 'AEC Installer Test', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_AUTHOR_EMAIL', 'aec-installer-test@example.invalid', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_COMMITTER_NAME', 'AEC Installer Test', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_COMMITTER_EMAIL', 'aec-installer-test@example.invalid', 'Process')
    & $customTarget init --repo $dataRepo --codex-home $codexHome | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'isolated AEC initialization failed'
    $skill = Join-Path $codexHome 'skills/aec/SKILL.md'
    Assert (Test-Path -LiteralPath $skill -PathType Leaf) 'AEC skill was not installed'
    Assert ([System.IO.File]::ReadAllText($runtimeAgents).Contains('<!-- AEC:BEGIN')) 'managed block was not installed'
    $canonicalAgents = Join-Path $dataRepo 'environment/providers/codex/AGENTS.md'
    $canonicalHash = (Get-FileHash -LiteralPath $canonicalAgents -Algorithm SHA256).Hash
    $configHash = (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash

    # Exercise the same Native AOT executable against Windows-style Copilot paths.
    # This validates AEC's local files without requiring Copilot CLI on the runner.
    $copilotHome = Join-Path $testRoot 'copilot-home'
    $null = New-Item -ItemType Directory -Path $copilotHome
    $runtimeCopilotInstructions = Join-Path $copilotHome 'copilot-instructions.md'
    [System.IO.File]::WriteAllText($runtimeCopilotInstructions, 'Personal Copilot instructions' + [Environment]::NewLine)
    & $customTarget init --repo $dataRepo --provider=copilot --copilot-home $copilotHome | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'isolated Copilot initialization failed'
    $canonicalCopilotInstructions = Join-Path $dataRepo 'environment/providers/copilot/copilot-instructions.md'
    $copilotSkill = Join-Path $copilotHome 'skills/aec/SKILL.md'
    Assert (Test-Path -LiteralPath $canonicalCopilotInstructions -PathType Leaf) 'canonical Copilot instructions were not created'
    Assert (Test-Path -LiteralPath $copilotSkill -PathType Leaf) 'Copilot AEC skill was not installed'
    Assert ((Get-FileHash -LiteralPath $runtimeCopilotInstructions -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath $canonicalCopilotInstructions -Algorithm SHA256).Hash) 'Copilot runtime and canonical instructions differ'
    Assert ([System.IO.File]::ReadAllText($runtimeCopilotInstructions).Contains('<!-- AEC:COPILOT:BEGIN')) 'Copilot managed block was not installed'
    Assert (-not (Test-Path -LiteralPath (Join-Path $copilotHome 'config.json'))) 'Copilot initialization created unmanaged config.json'

    & $uninstaller -CodexHome $codexHome | Out-Null
    Assert (-not (Test-Path -LiteralPath $customTarget)) 'uninstall left custom binary'
    Assert (-not (Test-Path -LiteralPath $uninstaller)) 'uninstall left helper'
    Assert (Test-Path -LiteralPath $config -PathType Leaf) 'uninstall removed runtime config'
    Assert ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -ceq $configHash) 'uninstall changed runtime config'
    Assert ([System.IO.File]::ReadAllText($runtimeAgents).Contains('Personal instructions')) 'uninstall removed personal instructions'
    Assert (-not ([System.IO.File]::ReadAllText($runtimeAgents).Contains('<!-- AEC:BEGIN'))) 'uninstall left managed block'
    Assert (-not (Test-Path -LiteralPath $skill)) 'uninstall left AEC skill'
    Assert ((Get-FileHash -LiteralPath $canonicalAgents -Algorithm SHA256).Hash -ceq $canonicalHash) 'uninstall changed data repository'
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

    Write-Output 'Windows installer lifecycle tests passed'
} finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    foreach ($name in $gitIdentityNames) {
        [Environment]::SetEnvironmentVariable($name, $originalGitIdentity[$name], 'Process')
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
