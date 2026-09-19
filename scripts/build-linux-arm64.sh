#!/bin/sh
# Build the Ubuntu Linux ARM64 Native AOT executable from this checkout.
# Run on an ARM64 Ubuntu machine with the .NET 10 SDK, clang, and zlib1g-dev.
set -eu

if [ "$#" -ne 0 ]; then
  echo "Usage: $0" >&2
  exit 1
fi

# Native AOT needs a linker and native libraries for its target platform.
# Reject another host or CPU rather than producing a misleading artifact.
if [ "$(uname -s)" != "Linux" ] || [ "$(uname -m)" != "aarch64" ]; then
  echo "Error: this build script supports only Linux on ARM64." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: the .NET 10 SDK is required to build aec." >&2
  exit 1
fi
if ! dotnet --list-sdks | grep -q '^10\.'; then
  echo "Error: the .NET 10 SDK is required to build aec." >&2
  exit 1
fi

if ! command -v clang >/dev/null 2>&1 || [ ! -f /usr/include/zlib.h ]; then
  echo "Error: clang and zlib1g-dev are required to build aec on Ubuntu." >&2
  exit 1
fi

# Resolve the checkout from the script location, not the caller's directory.
# Keep generated publication files under the ignored artifacts directory.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
output="$repo_root/artifacts/aec-linux-arm64"

dotnet publish "$repo_root/src/Aec/Aec.csproj" \
  --configuration Release \
  --runtime linux-arm64 \
  --self-contained true \
  -p:AssemblyName=aec \
  -p:PublishAot=true \
  --output "$output"

artifact="$output/aec"
if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  echo "Error: Native AOT build did not produce an executable at $artifact." >&2
  exit 1
fi

printf 'Built %s\n' "$artifact"
