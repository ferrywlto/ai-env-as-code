#!/bin/sh
set -eu

usage()
{
  echo "Usage: $0 [--install-dir ABSOLUTE_DIRECTORY]" >&2
}

if [ "$#" -eq 0 ]; then
  if [ -z "${HOME:-}" ]; then
    echo "Error: HOME is required when --install-dir is omitted." >&2
    exit 1
  fi

  install_dir="$HOME/.local/bin"
elif [ "$#" -eq 2 ] && [ "$1" = "--install-dir" ]; then
  install_dir="$2"
else
  usage
  exit 1
fi

case "$install_dir" in
  /*) ;;
  *)
    echo "Error: --install-dir must be an absolute path: $install_dir" >&2
    exit 1
    ;;
esac

if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
  echo "Error: this installer supports only macOS on ARM64." >&2
  exit 1
fi

# Resolve from the script location so the caller's working directory is irrelevant.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
artifact="$repo_root/artifacts/aec-osx-arm64/aec"
target="$install_dir/aec"

if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  echo "Error: build artifact is missing or not executable: $artifact" >&2
  echo "Run $repo_root/scripts/build-osx-arm64.sh first." >&2
  exit 1
fi

if ! "$artifact" --version >/dev/null 2>&1; then
  echo "Error: build artifact failed its version check: $artifact" >&2
  exit 1
fi

if [ -L "$target" ] || { [ -e "$target" ] && [ ! -f "$target" ]; }; then
  echo "Error: install target must be absent or a regular file: $target" >&2
  exit 1
fi

if [ -f "$target" ] && [ -x "$target" ] && cmp -s "$artifact" "$target"; then
  printf 'unchanged %s\n' "$target"
  exit 0
fi

mkdir -p -- "$install_dir"
temporary=$(mktemp "$install_dir/.aec.XXXXXX")

cleanup()
{
  if [ -n "${temporary:-}" ]; then
    rm -f -- "$temporary"
  fi
}

trap cleanup 0
trap 'cleanup; exit 1' HUP INT TERM

# Verify the complete replacement before the atomic move preserves an existing install.
cp -- "$artifact" "$temporary"
chmod 0755 "$temporary"
cmp -s "$artifact" "$temporary"
"$temporary" --version >/dev/null
mv -f -- "$temporary" "$target"
temporary=
trap - 0 HUP INT TERM

printf 'installed %s\n' "$target"
