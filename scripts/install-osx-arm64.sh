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

# The generated uninstaller stores this path inside a single-quoted shell value.
# Rejecting a quote is safer than producing a script which could interpret part of
# a filesystem path as shell code. Spaces remain fully supported.
case "$install_dir" in
  *"'"*)
    echo "Error: --install-dir cannot contain a single quote." >&2
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
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(dirname "$script_dir")

# These variables name the already-built source executable, its final location,
# and the uninstaller generated beside this install script. Quoting later uses
# keeps paths containing spaces as single arguments.
artifact="$repo_root/artifacts/aec-osx-arm64/aec"
target="$install_dir/aec"
uninstaller="$script_dir/uninstall-aec.sh"
uninstaller_marker='# AEC generated uninstaller version=1'

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

# The generated script is a managed file. Refuse any non-regular file and any
# regular file without our marker so a personal script is never overwritten.
if [ -L "$uninstaller" ] || { [ -e "$uninstaller" ] && [ ! -f "$uninstaller" ]; }; then
  echo "Error: generated uninstaller must be absent or a regular file: $uninstaller" >&2
  exit 1
fi
if [ -f "$uninstaller" ] && ! grep -Fqx "$uninstaller_marker" "$uninstaller"; then
  echo "Error: Refusing to overwrite non-AEC uninstaller: $uninstaller" >&2
  exit 1
fi

# The $aec skill invokes the executable by command name, so a custom directory
# must be present on the PATH inherited by Codex. The generated uninstaller does
# not rely on PATH: it remembers the exact selected executable path instead.
if [ -z "${HOME:-}" ] || [ "$install_dir" != "$HOME/.local/bin" ]; then
  printf 'warning: custom AEC install directory: %s\n' "$install_dir" >&2
  printf '%s\n' 'The $aec skill invokes `aec` through PATH.' >&2
  printf '%s\n' 'Ensure this directory is available to Codex, restart Codex, then run `aec help`.' >&2
fi

# Create and validate the uninstaller temporary file before changing the binary.
# This preflight catches a read-only source folder early, leaving an existing aec
# untouched when the companion script cannot be refreshed.
uninstaller_temporary=$(mktemp "$script_dir/.uninstall-aec.XXXXXX")
temporary=
cleanup()
{
  [ -z "${temporary:-}" ] || rm -f -- "$temporary"
  [ -z "${uninstaller_temporary:-}" ] || rm -f -- "$uninstaller_temporary"
}
trap cleanup 0
trap 'cleanup; exit 1' HUP INT TERM

# The here-document writes fixed script text. printf supplies the one variable
# value which must match this installation's exact aec executable location.
{
  printf '%s\n' '#!/bin/sh' "$uninstaller_marker"
  printf '%s\n' '# This file was generated by install-osx-arm64.sh. Do not edit it.'
  printf "installed_aec='%s'\n" "$target"
  cat <<'EOF'
# This script first lets AEC remove its managed runtime integration. Only when
# that succeeds does it remove the installed executable and then this script.
set -eu

usage()
{
  echo "Usage: $0 [--codex-home ABSOLUTE_PATH]" >&2
}

codex_home=
if [ "$#" -eq 0 ]; then
  :
elif [ "$#" -eq 2 ] && [ "$1" = "--codex-home" ] && [ -n "$2" ]; then
  codex_home="$2"
  case "$codex_home" in
    /*) ;;
    *)
      echo "Error: --codex-home must be an absolute path: $codex_home" >&2
      exit 1
      ;;
  esac
else
  usage
  exit 1
fi

# Run through an explicit path so the cleanup works even when the install folder
# is not on PATH. A missing or replaced executable must not delete this helper.
if [ ! -f "$installed_aec" ] || [ ! -x "$installed_aec" ] || [ -L "$installed_aec" ]; then
  echo "Error: installed aec executable is missing or unsafe: $installed_aec" >&2
  exit 1
fi

if [ -n "$codex_home" ]; then
  "$installed_aec" uninstall --codex-home "$codex_home"
else
  "$installed_aec" uninstall
fi

# The caller must use a path such as ./scripts/uninstall-aec.sh, rather than a
# bare command name, so $0 identifies this exact generated file for safe cleanup.
case "$0" in
  */*) ;;
  *)
    echo "Error: run this uninstaller by its explicit path." >&2
    exit 1
    ;;
esac
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_path="$script_dir/$(basename -- "$0")"

rm -f -- "$installed_aec"
rm -f -- "$script_path"
EOF
} >"$uninstaller_temporary"
chmod 0755 "$uninstaller_temporary"
sh -n "$uninstaller_temporary"

# Decide each output independently. A repaired helper can be written even when
# the executable already matches the artifact byte-for-byte.
needs_binary_install=true
if [ -f "$target" ] && [ -x "$target" ] && cmp -s "$artifact" "$target"; then
  needs_binary_install=false
fi
needs_uninstaller_install=true
if [ -f "$uninstaller" ] && cmp -s "$uninstaller_temporary" "$uninstaller"; then
  needs_uninstaller_install=false
fi

if [ "$needs_binary_install" = true ]; then
  # mkdir -p creates the directory and any missing parents but succeeds if they
  # already exist. The temporary sibling makes the final move atomic.
  mkdir -p -- "$install_dir"
  temporary=$(mktemp "$install_dir/.aec.XXXXXX")
  cp -- "$artifact" "$temporary"
  chmod 0755 "$temporary"
  cmp -s "$artifact" "$temporary"
  "$temporary" version >/dev/null
  mv -f -- "$temporary" "$target"
  temporary=
  printf 'installed %s\n' "$target"
fi

if [ "$needs_uninstaller_install" = true ]; then
  # This move is also atomic because the temporary and final file share a folder.
  mv -f -- "$uninstaller_temporary" "$uninstaller"
  uninstaller_temporary=
  printf 'installed %s\n' "$uninstaller"
fi

if [ "$needs_binary_install" = false ] && [ "$needs_uninstaller_install" = false ]; then
  printf 'unchanged %s\n' "$target"
fi

trap - 0 HUP INT TERM
