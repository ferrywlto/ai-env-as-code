#!/bin/sh
# The first line tells macOS to run this file with its standard POSIX shell.
# -e stops after a failed command; -u stops when an unset variable is expanded.
set -eu

# Assume I run this script from user home e.g. /Users/{Username} or ~ and the script located at ./Documents/GitHub/{ProjectRepo}/scripts/build.sh
# The path passed to call this script will be ./Documents/GitHub/{ProjectRepo}/scripts/build.sh
# Then $(dirname -- "$0") will return ./Documents/GitHub/{ProjectRepo}/scripts/
# Assume this script placed in a folder in the repo (e.g. scripts), the repo is one level upper
# $(dirname -- "$0")/.. will return ./Documents/GitHub/{ProjectRepo}/scripts/.. which is ./Documents/GitHub/{ProjectRepo}/
# The current working directory (pwd)" will be /Users/{Username}
# Combined together: $(dirname -- "$0")/.. && pwd will return absolute path /Users/{Username}/./Documents/GitHub/{ProjectRepo}/scripts/..
# which equals to /Users/{Username}/Documents/GitHub/{ProjectRepo}/

# $0 is the path used to invoke this script. $(...) runs the enclosed commands
# and stores their output. Clearing CDPATH prevents a user's shell configuration
# from changing cd's behavior or adding unwanted output.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

# $# is the number of supplied arguments. This build has no command-line options,
# so any argument is an error. >&2 sends the explanation to standard error, and
# exit 1 reports failure to the calling shell or automation.
if [ "$#" -ne 0 ]; then
  echo "Usage: $0" >&2
  exit 1
fi

# uname -s identifies the operating system and uname -m identifies the CPU.
# Native AOT is platform-specific, so stop unless both match the supported target.
if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
  echo "Error: this build script supports only macOS on ARM64." >&2
  exit 1
fi

# command -v searches PATH without starting dotnet. ! reverses the result so this
# block runs only when dotnet is unavailable; >/dev/null 2>&1 hides probe output.
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET 10 SDK is required to build aec." >&2
  exit 1
fi

# xcrun is Apple's developer-tool launcher. The first check finds xcrun and the
# second asks it to locate clang, the native compiler required by Native AOT.
if ! command -v xcrun >/dev/null 2>&1 ||
  ! xcrun --find clang >/dev/null 2>&1; then
  echo "Error: Xcode Command Line Tools are required to build aec." >&2
  exit 1
fi

# A trailing \ continues one command onto the next line. These publish settings:
# - select the Aec project and its optimized Release configuration;
# - target Apple-silicon macOS and include the required .NET runtime components;
# - name the executable "aec" and compile it ahead of time into native machine code;
# - place all generated publication files in the repository's ignored artifacts folder.
dotnet publish "$repo_root/src/Aec/Aec.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  -p:AssemblyName=aec \
  -p:PublishAot=true \
  --output "$repo_root/artifacts/aec-osx-arm64"

# Record the exact expected result. -f requires a regular file and -x requires
# execute permission; || means either failed check enters the error block.
artifact="$repo_root/artifacts/aec-osx-arm64/aec"
if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  echo "Error: Native AOT build did not produce an executable at $artifact." >&2
  exit 1
fi

# %s inserts the path without interpreting it, and \n ends the success message.
printf 'Built %s\n' "$artifact"
