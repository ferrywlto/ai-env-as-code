#!/bin/sh
# This integration test runs the real macOS ARM64 installer against temporary
# directories. It requires the Native AOT artifact produced by the build script.
set -eu

fail()
{
  printf 'test failed: %s\n' "$1" >&2
  exit 1
}

# Resolve the repository from this test file so callers may run it from any
# working directory. Clearing CDPATH prevents shell configuration from adding
# output or changing how cd resolves the path.
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
installer="$repo_root/scripts/install-osx-arm64.sh"
artifact="$repo_root/artifacts/aec-osx-arm64/aec"

if [ ! -f "$artifact" ] || [ ! -x "$artifact" ]; then
  fail "build artifact is missing; run $repo_root/scripts/build-osx-arm64.sh first"
fi

# Every installation stays under one unique temporary root. The cleanup trap
# removes that exact directory after success, failure, or interruption.
test_root=$(mktemp -d "${TMPDIR:-/tmp}/aec-install-test.XXXXXX")
cleanup()
{
  if [ -n "${test_root:-}" ] && [ -d "$test_root" ]; then
    rm -R -- "$test_root"
  fi
}
trap cleanup 0
trap 'exit 1' HUP INT TERM

assert_single_line()
{
  expected="$1"
  actual_file="$2"
  expected_file="$test_root/expected-line"
  printf '%s\n' "$expected" >"$expected_file"
  cmp -s "$expected_file" "$actual_file" || fail "unexpected output in $actual_file"
}

assert_empty()
{
  [ ! -s "$1" ] || fail "expected no output in $1"
}

assert_installed_copy()
{
  installed="$1"
  [ -f "$installed" ] && [ -x "$installed" ] || fail "installed executable is missing: $installed"
  cmp -s "$artifact" "$installed" || fail "installed bytes differ from the build artifact: $installed"
}

assert_custom_warning()
{
  directory="$1"
  actual_file="$2"
  expected_file="$test_root/expected-warning"
  {
    printf 'warning: custom AEC install directory: %s\n' "$directory"
    printf '%s\n' 'The $aec skill invokes `aec` through PATH.'
    printf '%s\n' 'Ensure this directory is available to Codex, restart Codex, then run `aec help`.'
  } >"$expected_file"
  cmp -s "$expected_file" "$actual_file" || fail "unexpected custom-directory warning"
}

# The default destination must remain quiet.
default_home="$test_root/default home"
stdout_file="$test_root/default.stdout"
stderr_file="$test_root/default.stderr"
HOME="$default_home" "$installer" >"$stdout_file" 2>"$stderr_file"
assert_single_line "installed $default_home/.local/bin/aec" "$stdout_file"
assert_empty "$stderr_file"
assert_installed_copy "$default_home/.local/bin/aec"

# Passing the default destination explicitly is still a default installation.
HOME="$default_home" "$installer" --install-dir "$default_home/.local/bin" >"$stdout_file" 2>"$stderr_file"
assert_single_line "unchanged $default_home/.local/bin/aec" "$stdout_file"
assert_empty "$stderr_file"

# A custom path, including one with spaces, warns on both install and reinstall.
custom_dir="$test_root/custom bin"
stdout_file="$test_root/custom.stdout"
stderr_file="$test_root/custom.stderr"
HOME="$default_home" "$installer" --install-dir "$custom_dir" >"$stdout_file" 2>"$stderr_file"
assert_single_line "installed $custom_dir/aec" "$stdout_file"
assert_custom_warning "$custom_dir" "$stderr_file"
assert_installed_copy "$custom_dir/aec"

HOME="$default_home" "$installer" --install-dir "$custom_dir" >"$stdout_file" 2>"$stderr_file"
assert_single_line "unchanged $custom_dir/aec" "$stdout_file"
assert_custom_warning "$custom_dir" "$stderr_file"

# An explicit custom destination remains valid when HOME is unavailable.
no_home_dir="$test_root/no-home"
stdout_file="$test_root/no-home.stdout"
stderr_file="$test_root/no-home.stderr"
(unset HOME; "$installer" --install-dir "$no_home_dir") >"$stdout_file" 2>"$stderr_file"
assert_single_line "installed $no_home_dir/aec" "$stdout_file"
assert_custom_warning "$no_home_dir" "$stderr_file"
assert_installed_copy "$no_home_dir/aec"

printf 'installer warning tests passed\n'
