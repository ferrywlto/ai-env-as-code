# Build the Windows x64 Native AOT executable from this source checkout.
# Run this script in PowerShell on an x64 Windows machine with the .NET 10 SDK
# and Visual Studio's Desktop development with C++ workload installed.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ($args.Count -ne 0) {
    throw 'Usage: ./scripts/build-win-x64.ps1'
}

# Native AOT needs the target operating system's native linker. Refuse to
# produce a misleading artifact on another host or CPU architecture.
$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$isX64 = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::X64
if (-not $isWindows -or -not $isX64) {
    throw 'This build script supports only Windows on x64.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required to build aec.'
}

$installedSdks = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($installedSdks -match '^10\.')) {
    throw 'The .NET 10 SDK is required to build aec.'
}

# $PSScriptRoot is the absolute directory containing this script, regardless
# of the caller's current directory. Keep generated output in the ignored
# artifacts folder rather than alongside source files.
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/Aec/Aec.csproj'
$output = Join-Path $repoRoot 'artifacts/aec-win-x64'

& dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    '-p:AssemblyName=aec' `
    '-p:PublishAot=true' `
    --output $output

# External programs can return a failure code without raising a PowerShell
# exception, so check it explicitly before claiming the build succeeded.
if ($LASTEXITCODE -ne 0) {
    throw "Native AOT publish failed with exit code $LASTEXITCODE."
}

$artifact = Join-Path $output 'aec.exe'
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
    throw "Native AOT build did not produce an executable at $artifact."
}

Write-Output "Built $artifact"
