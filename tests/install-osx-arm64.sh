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

# pwd -P normalizes TMPDIR and resolves macOS's /var -> /private/var link so
# AEC's intentional symlink-path guard accepts this disposable test fixture.
test_root=$(CDPATH= cd -- "$(mktemp -d "${TMPDIR:-/tmp}/aec-installer-tests.XXXXXX")" && pwd -P)
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

# AEC initialization creates commits. Give the disposable process its own Git
# identity and disable signing so this test never depends on the user's Git setup.
git_config="$test_root/gitconfig"
printf '%s\n' \
    '[user]' \
    '    name = AEC macOS Smoke' \
    '    email = aec-macos-smoke@example.invalid' \
    '[commit]' \
    '    gpgSign = false' >"$git_config"
export GIT_CONFIG_GLOBAL="$git_config"
export GIT_CONFIG_NOSYSTEM=1

# Exercise the Native AOT executable against disposable Codex and Copilot homes.
# This validates AEC's local file lifecycle without requiring Copilot CLI itself.
codex_home="$test_root/codex-home"
data_repo="$test_root/aec-data"
mkdir -p "$codex_home"
printf '%s\n' 'Personal Codex instructions' >"$codex_home/AGENTS.md"
printf '%s\n' 'personality = "none"' >"$codex_home/config.toml"
"$custom_target" init --repo "$data_repo" --codex-home "$codex_home" >/dev/null

copilot_home="$test_root/copilot-home"
runtime_copilot="$copilot_home/copilot-instructions.md"
canonical_copilot="$data_repo/environment/providers/copilot/copilot-instructions.md"
copilot_skill="$copilot_home/skills/aec/SKILL.md"
mkdir -p "$copilot_home"
printf '%s\n' 'Personal Copilot instructions' >"$runtime_copilot"
"$custom_target" init --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" >/dev/null

[ -f "$canonical_copilot" ] || fail "canonical Copilot instructions were not created"
[ -f "$copilot_skill" ] || fail "Copilot AEC skill was not installed"
assert_file_equals "$canonical_copilot" "$runtime_copilot"
grep -F '<!-- AEC:COPILOT:BEGIN' "$runtime_copilot" >/dev/null || \
    fail "Copilot managed block was not installed"
assert_empty "$copilot_home/config.json"

"$custom_target" status --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" \
    >"$test_root/copilot-status"
printf '%s\n' 'copilot/copilot-instructions.md in_sync' >"$test_root/copilot-status-expected"
assert_output "$test_root/copilot-status-expected" "$test_root/copilot-status"

# Backup must copy runtime drift into the canonical file and create exactly one
# source-of-truth commit with the documented subject.
printf '\n%s\n' 'macOS backup drift' >>"$runtime_copilot"
"$custom_target" backup --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" \
    >"$test_root/copilot-backup"
grep -E '^committed [0-9a-f]+$' "$test_root/copilot-backup" >/dev/null || \
    fail "Copilot backup did not report its commit"
assert_file_equals "$canonical_copilot" "$runtime_copilot"
[ "$(git -C "$data_repo" log -1 --format=%s)" = 'Backup Copilot instructions' ] || \
    fail "Copilot backup used an unexpected commit subject"

# Apply must restore the committed canonical bytes without changing Git HEAD.
head_before_apply=$(git -C "$data_repo" rev-parse HEAD)
printf '%s\n' 'macOS apply drift' >>"$runtime_copilot"
"$custom_target" apply --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" \
    >"$test_root/copilot-apply"
printf '%s\n' 'applied' >"$test_root/copilot-apply-expected"
assert_output "$test_root/copilot-apply-expected" "$test_root/copilot-apply"
assert_file_equals "$canonical_copilot" "$runtime_copilot"
[ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || \
    fail "Copilot apply changed repository HEAD"

"$custom_target" status --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" \
    >"$test_root/copilot-final-status"
assert_output "$test_root/copilot-status-expected" "$test_root/copilot-final-status"

# A small fake binary makes the generated script's all-or-nothing cleanup testable
# without changing the actual personal Codex environment.
fake_source="$test_root/fake-source"
fake_artifact="$test_root/fake-aec"
mkdir -p "$fake_source/scripts" "$fake_source/artifacts/aec-osx-arm64"
cp "$source_installer" "$fake_source/scripts/install-osx-arm64.sh"
printf '%s\n' '#!/bin/sh' \
    'if [ "$1" = "version" ]; then printf "2.0.0-alpha.3\\n"; exit 0; fi' \
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
