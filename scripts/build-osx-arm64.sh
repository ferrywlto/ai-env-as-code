#!/bin/sh
set -eu

# Resolve from the script location so the caller's working directory is irrelevant.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if [ "$#" -ne 0 ]; then
  echo "Usage: $0" >&2
  exit 1
fi

if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
  echo "Error: this build script supports only macOS on ARM64." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET 10 SDK is required to build aec." >&2
  exit 1
fi

if ! command -v xcrun >/dev/null 2>&1 ||
  ! xcrun --find clang >/dev/null 2>&1; then
  echo "Error: Xcode Command Line Tools are required to build aec." >&2
  exit 1
fi

dotnet publish "$repo_root/src/Aec/Aec.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  -p:AssemblyName=aec \
  -p:PublishAot=true \
  --output "$repo_root/artifacts/aec-osx-arm64"

artifact="$repo_root/artifacts/aec-osx-arm64/aec"
if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  echo "Error: Native AOT build did not produce an executable at $artifact." >&2
  exit 1
fi

printf 'Built %s\n' "$artifact"
