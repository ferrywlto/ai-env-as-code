#!/bin/sh
# The first line tells macOS to run this file with its standard POSIX shell.
# -e stops after a failed command; -u stops when an unset variable is expanded.
set -eu

# A shell function groups reusable commands. This one prints the supported syntax.
# >&2 sends usage text to standard error because it explains an invalid invocation.
usage()
{
  echo "Usage: $0 [--install-dir ABSOLUTE_DIRECTORY]" >&2
}

# $# is the number of supplied arguments. With no arguments, install into the
# conventional per-user binary directory. ${HOME:-} safely expands to an empty
# value instead of triggering set -u when HOME is unavailable.
if [ "$#" -eq 0 ]; then
  if [ -z "${HOME:-}" ]; then
    echo "Error: HOME is required when --install-dir is omitted." >&2
    exit 1
  fi

  install_dir="$HOME/.local/bin"
# The only accepted option consists of exactly two arguments: its name and value.
# $1 is the option name and $2 is the requested installation directory.
elif [ "$#" -eq 2 ] && [ "$1" = "--install-dir" ]; then
  install_dir="$2"
else
  usage
  exit 1
fi

# Shell case patterns make the leading / mandatory, which means the directory is
# absolute. ;; ends a matching branch; * catches every non-absolute value.
case "$install_dir" in
  /*) ;;
  *)
    echo "Error: --install-dir must be an absolute path: $install_dir" >&2
    exit 1
    ;;
esac

# uname -s identifies the operating system and uname -m identifies the CPU.
# The published native executable can run only on the platform it targets.
if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
  echo "Error: this installer supports only macOS on ARM64." >&2
  exit 1
fi

# $0 is the path used to invoke this script. $(...) runs the enclosed commands
# and stores their output. Clearing CDPATH prevents a user's shell configuration
# from changing cd's behavior or adding unwanted output.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

# These variables name the already-built source executable and its final location.
# Quoting their later expansions keeps paths containing spaces as single arguments.
artifact="$repo_root/artifacts/aec-osx-arm64/aec"
target="$install_dir/aec"

# -f requires a regular file and -x requires execute permission. || means either
# failed check enters this block, so installation never copies a missing build.
if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  echo "Error: build artifact is missing or not executable: $artifact" >&2
  echo "Run $repo_root/scripts/build-osx-arm64.sh first." >&2
  exit 1
fi

# Start the artifact with its read-only version command before trusting it. ! makes
# this block run only when that command fails; its normal output is discarded.
if ! "$artifact" version >/dev/null 2>&1; then
  echo "Error: build artifact failed its version check: $artifact" >&2
  exit 1
fi

# -L detects a symbolic link. If the target otherwise exists, -f requires it to be
# a regular file. This prevents replacing a directory or following a linked target.
if [ -L "$target" ] || { [ -e "$target" ] && [ ! -f "$target" ]; }; then
  echo "Error: install target must be absent or a regular file: $target" >&2
  exit 1
fi

# The $aec skill invokes the executable by command name, so a custom directory
# must be present on the PATH inherited by Codex. `||` skips its right side when
# HOME is unavailable, avoiding an unset expansion while treating the path as custom.
if [ -z "${HOME:-}" ] || [ "$install_dir" != "$HOME/.local/bin" ]; then
  printf 'warning: custom AEC install directory: %s\n' "$install_dir" >&2
  printf '%s\n' 'The $aec skill invokes `aec` through PATH.' >&2
  printf '%s\n' 'Ensure this directory is available to Codex, restart Codex, then run `aec help`.' >&2
fi

# cmp -s compares bytes without printing them. An identical executable is already
# installed, so return success without rewriting it or changing its timestamp.
if [ -f "$target" ] && [ -x "$target" ] && cmp -s "$artifact" "$target"; then
  printf 'unchanged %s\n' "$target"
  exit 0
fi

# mkdir -p creates the directory and any missing parents but succeeds if they
# already exist. -- marks the end of command options. mktemp then creates a unique
# sibling file; keeping it beside the target makes the later move stay atomic.
mkdir -p -- "$install_dir"
temporary=$(mktemp "$install_dir/.aec.XXXXXX")

# Remove an unfinished temporary file. ${temporary:-} also remains safe under
# set -u if cleanup runs before the variable has a usable value.
cleanup()
{
  if [ -n "${temporary:-}" ]; then
    rm -f -- "$temporary"
  fi
}

# Signal 0 means normal shell exit, while HUP, INT, and TERM are common interruption
# signals. These traps ensure failed or interrupted installs leave no temporary file.
trap cleanup 0
trap 'cleanup; exit 1' HUP INT TERM

# Prepare and validate the replacement completely before touching an existing aec:
# copy the bytes, make the temporary file executable, compare it with the artifact,
# and run its version check. A failure stops here and cleanup removes the temporary.
cp -- "$artifact" "$temporary"
chmod 0755 "$temporary"
cmp -s "$artifact" "$temporary"
"$temporary" version >/dev/null

# Because the temporary and target files share a directory, mv performs the final
# replacement as one filesystem operation. Clearing the variable prevents cleanup
# from deleting the installed file; trap - restores the shell's original handlers.
mv -f -- "$temporary" "$target"
temporary=
trap - 0 HUP INT TERM

# %s inserts the path without interpreting it, and \n ends the success message.
printf 'installed %s\n' "$target"
