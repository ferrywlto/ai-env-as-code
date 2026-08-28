#!/bin/sh

# This test exercises the installer from a disposable copy of the source tree.
# The real generated uninstaller is therefore never written into this checkout.
set -eu

fail() {
    printf '%s\n' "FAIL: $*" >&2
    exit 1
}

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_installer="$repo_root/scripts/install-osx-arm64.sh"
source_artifact="$repo_root/artifacts/aec-osx-arm64/aec"

[ -f "$source_installer" ] || fail "installer was not found: $source_installer"
[ -x "$source_artifact" ] || fail "Native AOT artifact was not found: $source_artifact"

# pwd normalizes a trailing slash in TMPDIR so expected paths match the installer.
test_root=$(CDPATH= cd -- "$(mktemp -d "${TMPDIR:-/tmp}/aec-installer-tests.XXXXXX")" && pwd)
cleanup() {
    rm -rf -- "$test_root"
}
trap cleanup EXIT HUP INT TERM

assert_file_equals() {
    expected=$1
    actual=$2
    cmp -s "$expected" "$actual" || fail "files differ: $actual"
}

assert_output() {
    expected=$1
    actual=$2
    cmp -s "$expected" "$actual" || fail "unexpected command output: $actual"
}

assert_empty() {
    [ ! -e "$1" ] || fail "expected path to be absent: $1"
}

prepare_source() {
    source_root=$1
    artifact=$2
    mkdir -p "$source_root/scripts" "$source_root/artifacts/aec-osx-arm64"
    cp "$source_installer" "$source_root/scripts/install-osx-arm64.sh"
    cp "$artifact" "$source_root/artifacts/aec-osx-arm64/aec"
    chmod +x "$source_root/scripts/install-osx-arm64.sh" "$source_root/artifacts/aec-osx-arm64/aec"
}

assert_generated_uninstaller() {
    script=$1
    expected_binary=$2
    [ -x "$script" ] || fail "generated uninstaller was not executable: $script"
    grep -F "installed_aec='$expected_binary'" "$script" >/dev/null || \
        fail "generated uninstaller did not embed the selected binary path"
    grep -F '"$installed_aec" uninstall' "$script" >/dev/null || \
        fail "generated uninstaller did not invoke the binary directly"
}

source_root="$test_root/source"
prepare_source "$source_root" "$source_artifact"
installer="$source_root/scripts/install-osx-arm64.sh"
generated_uninstaller="$source_root/scripts/uninstall-aec.sh"
default_target="$test_root/home/.local/bin/aec"
default_dir=$(dirname "$default_target")

"$installer" --install-dir "$default_dir" >"$test_root/initial-output"
printf 'installed %s\ninstalled %s\n' "$default_target" "$generated_uninstaller" >"$test_root/initial-expected"
assert_output "$test_root/initial-expected" "$test_root/initial-output"
assert_file_equals "$source_artifact" "$default_target"
assert_generated_uninstaller "$generated_uninstaller" "$default_target"

"$installer" --install-dir "$default_dir" >"$test_root/unchanged-output"
printf 'unchanged %s\n' "$default_target" >"$test_root/unchanged-expected"
assert_output "$test_root/unchanged-expected" "$test_root/unchanged-output"

rm -f -- "$generated_uninstaller"
"$installer" --install-dir "$default_dir" >"$test_root/recovered-output"
printf 'installed %s\n' "$generated_uninstaller" >"$test_root/recovered-expected"
assert_output "$test_root/recovered-expected" "$test_root/recovered-output"
assert_generated_uninstaller "$generated_uninstaller" "$default_target"

custom_dir="$test_root/custom install/bin"
custom_target="$custom_dir/aec"
"$installer" --install-dir "$custom_dir" >"$test_root/custom-output" 2>"$test_root/custom-error"
printf 'installed %s\ninstalled %s\n' "$custom_target" "$generated_uninstaller" >"$test_root/custom-expected"
assert_output "$test_root/custom-expected" "$test_root/custom-output"
assert_file_equals "$source_artifact" "$custom_target"
[ -f "$default_target" ] || fail "switching install directories removed the old binary"
grep -F "warning: custom AEC install directory: $custom_dir" "$test_root/custom-error" >/dev/null || \
    fail "custom installation warning was missing"
assert_generated_uninstaller "$generated_uninstaller" "$custom_target"

conflict_source="$test_root/conflict-source"
prepare_source "$conflict_source" "$source_artifact"
printf 'personal script\n' >"$conflict_source/scripts/uninstall-aec.sh"
if "$conflict_source/scripts/install-osx-arm64.sh" --install-dir "$test_root/conflict/bin" >"$test_root/conflict-output" 2>"$test_root/conflict-error"; then
    fail "installer overwrote a non-AEC uninstaller"
fi
assert_empty "$test_root/conflict/bin/aec"
grep -F 'Refusing to overwrite non-AEC uninstaller' "$test_root/conflict-error" >/dev/null || \
    fail "conflict error did not explain the preserved file"

# A small fake binary makes the generated script's all-or-nothing cleanup testable
# without changing the actual personal Codex environment.
fake_source="$test_root/fake-source"
fake_artifact="$test_root/fake-aec"
mkdir -p "$fake_source/scripts" "$fake_source/artifacts/aec-osx-arm64"
cp "$source_installer" "$fake_source/scripts/install-osx-arm64.sh"
printf '%s\n' '#!/bin/sh' \
    'if [ "$1" = "version" ]; then printf "1.2.1\\n"; exit 0; fi' \
    'if [ "$AEC_TEST_UNINSTALL_RESULT" = "fail" ]; then exit 17; fi' \
    'printf "%s\\n" "$*" > "$AEC_TEST_ARGS_FILE"' \
    'exit 0' >"$fake_artifact"
cp "$fake_artifact" "$fake_source/artifacts/aec-osx-arm64/aec"
chmod +x "$fake_source/scripts/install-osx-arm64.sh" "$fake_source/artifacts/aec-osx-arm64/aec"

fake_target_dir="$test_root/fake-bin"
fake_target="$fake_target_dir/aec"
"$fake_source/scripts/install-osx-arm64.sh" --install-dir "$fake_target_dir" >"$test_root/fake-install-output"
fake_uninstaller="$fake_source/scripts/uninstall-aec.sh"
fake_codex_home="$test_root/fake-codex-home"
mkdir -p "$fake_codex_home"
printf 'runtime config remains owned by aec\n' >"$fake_codex_home/config.toml"
data_sentinel="$test_root/data-repository-sentinel"
printf 'canonical data remains untouched\n' >"$data_sentinel"

if "$fake_uninstaller" --codex-home relative >"$test_root/invalid-output" 2>"$test_root/invalid-error"; then
    fail "generated uninstaller accepted a relative --codex-home"
fi
[ -f "$fake_target" ] || fail "invalid arguments removed the binary"
[ -f "$fake_uninstaller" ] || fail "invalid arguments removed the uninstaller"

if AEC_TEST_UNINSTALL_RESULT=fail AEC_TEST_ARGS_FILE="$test_root/fake-args" "$fake_uninstaller" >"$test_root/failure-output" 2>"$test_root/failure-error"; then
    fail "generated uninstaller continued after aec uninstall failed"
fi
[ -f "$fake_target" ] || fail "failed aec uninstall removed the binary"
[ -f "$fake_uninstaller" ] || fail "failed aec uninstall removed the uninstaller"

AEC_TEST_UNINSTALL_RESULT=success AEC_TEST_ARGS_FILE="$test_root/fake-args" "$fake_uninstaller" --codex-home "$fake_codex_home"
assert_empty "$fake_target"
assert_empty "$fake_uninstaller"
[ -f "$fake_codex_home/config.toml" ] || fail "generated uninstaller removed config.toml"
[ -f "$data_sentinel" ] || fail "generated uninstaller removed data repository content"
printf 'uninstall --codex-home %s\n' "$fake_codex_home" >"$test_root/fake-args-expected"
assert_output "$test_root/fake-args-expected" "$test_root/fake-args"

printf '%s\n' 'installer generated-uninstaller tests passed'
