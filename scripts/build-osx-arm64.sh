#!/bin/sh
set -eu

# Assume I run this script from user home e.g. /Users/{Username} or ~ and the script located at ./Documents/GitHub/{ProjectRepo}/scripts/build.sh
# The path passed to call this script will be ./Documents/GitHub/{ProjectRepo}/scripts/build.sh
# Then $(dirname -- "$0") will return ./Documents/GitHub/{ProjectRepo}/scripts/
# Assume this script placed in a folder in the repo (e.g. scripts), the repo is one level upper
# $(dirname -- "$0")/.. will return ./Documents/GitHub/{ProjectRepo}/scripts/.. which is ./Documents/GitHub/{ProjectRepo}/
# The current working directory (pwd)" will be /Users/{Username}
# Combined together: $(dirname -- "$0")/.. && pwd will return absolute path /Users/{Username}/./Documents/GitHub/{ProjectRepo}/scripts/..
# which equals to /Users/{Username}/Documents/GitHub/{ProjectRepo}/

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
