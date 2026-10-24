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
printf '%s\n' 'Shared approval.' 'Local paths.' 'Codex models.' >"$codex_home/AGENTS.md"
printf '%s\n' 'model = "keep-local-model"' 'personality = "friendly"' \
    '[features]' 'local_setting = true' >"$codex_home/config.toml"
cp "$codex_home/config.toml" "$test_root/codex-config-original"
"$custom_target" init --repo "$data_repo" --codex-home "$codex_home" >/dev/null

copilot_home="$test_root/copilot-home"
runtime_copilot="$copilot_home/copilot-instructions.md"
canonical_copilot="$data_repo/environment/providers/copilot/copilot-instructions.md"
copilot_skill="$copilot_home/skills/aec/SKILL.md"
mkdir -p "$copilot_home"
printf '%s\n' 'Shared approval.' 'Local paths.' 'Copilot tools.' >"$runtime_copilot"
printf '%s\n' '{"localSetting":true}' >"$copilot_home/config.json"
cp "$copilot_home/config.json" "$test_root/copilot-config-original"
"$custom_target" init --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" >/dev/null

[ -f "$canonical_copilot" ] || fail "canonical Copilot instructions were not created"
[ -f "$copilot_skill" ] || fail "Copilot AEC skill was not installed"
assert_file_equals "$canonical_copilot" "$runtime_copilot"
grep -F '<!-- AEC:COPILOT:BEGIN' "$runtime_copilot" >/dev/null || \
    fail "Copilot managed block was not installed"
# Ordinary init retains the historical canonical files. Commit Copilot's
# baseline before authoring the reviewed split; public 2.0 commands require
# committed portable targets rather than silently using these legacy files.
git -C "$data_repo" add -- environment/providers/copilot/copilot-instructions.md
git -C "$data_repo" commit -m 'Record smoke Copilot baseline' >/dev/null
cp "$codex_home/AGENTS.md" "$test_root/codex-legacy-original"
cp "$runtime_copilot" "$test_root/copilot-legacy-original"
platform=macos-arm64
shared_source="$data_repo/environment/shared/instructions.md"
platform_source="$data_repo/environment/platforms/$platform/policy.md"
mkdir -p "$(dirname "$shared_source")" "$(dirname "$platform_source")"
printf '%s\n' 'Shared approval.' >"$shared_source"
printf '%s\n' 'Local paths.' >"$platform_source"
printf '%s\n' 'Codex models.' >"$data_repo/environment/providers/codex/overlay.md"
printf '%s\n' 'Copilot tools.' >"$data_repo/environment/providers/copilot/overlay.md"
git -C "$data_repo" add -- environment/shared/instructions.md \
    "environment/platforms/$platform/policy.md" environment/providers/codex/overlay.md \
    environment/providers/copilot/overlay.md
git -C "$data_repo" commit -m 'Author smoke instruction sources' >/dev/null

# Keep provider selection in one place so both Native AOT lifecycle cases
# execute the same commands, always with explicit disposable repo/home paths.
run_provider() {
    case "$1" in
        codex) shift; "$custom_target" "$@" --repo "$data_repo" --codex-home "$codex_home" ;;
        copilot) shift; "$custom_target" "$@" --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" ;;
    esac
}

# Enrollment writes only one target and never deploys or commits it. Review
# and commit that target before enrolling the next provider.
for provider in codex copilot; do
    case "$provider" in
        codex) instruction_file=AGENTS.md ;;
        copilot) instruction_file=copilot-instructions.md ;;
    esac
    target_relative="environment/targets/$platform/$provider/$instruction_file"
    head_before_enroll=$(git -C "$data_repo" rev-parse HEAD)
    run_provider "$provider" init --enroll-shared >"$test_root/enroll-output"
    printf 'enrolled %s\n' "$target_relative" >"$test_root/enroll-expected"
    assert_output "$test_root/enroll-expected" "$test_root/enroll-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_enroll" ] || fail "enrollment committed"
    git -C "$data_repo" diff --cached --quiet || fail "enrollment staged changes"
    assert_file_equals "$test_root/codex-legacy-original" "$codex_home/AGENTS.md"
    assert_file_equals "$test_root/copilot-legacy-original" "$runtime_copilot"
    if [ "$provider" = codex ]; then
        assert_empty "$data_repo/environment/targets/$platform/copilot"
    fi
    git -C "$data_repo" add -- "$target_relative"
    git -C "$data_repo" commit -m "Enroll smoke $provider target" >/dev/null
done

for provider in codex copilot; do
    case "$provider" in
        codex)
            runtime="$codex_home/AGENTS.md"
            instruction_file=AGENTS.md
            original_line='Codex models.'
            updated_line='Codex models updated.'
            commit_subject='Backup portable Codex environment'
            ;;
        copilot)
            runtime="$runtime_copilot"
            instruction_file=copilot-instructions.md
            original_line='Copilot tools.'
            updated_line='Copilot tools updated.'
            commit_subject='Backup portable instructions'
            ;;
    esac
    target_relative="environment/targets/$platform/$provider/$instruction_file"
    overlay_relative="environment/providers/$provider/overlay.md"
    head_before_apply=$(git -C "$data_repo" rev-parse HEAD)
    run_provider "$provider" apply >"$test_root/apply-output"
    printf '%s\n' applied >"$test_root/apply-expected"
    assert_output "$test_root/apply-expected" "$test_root/apply-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "initial apply changed HEAD"
    run_provider "$provider" status >"$test_root/status-output"
    if [ "$provider" = codex ]; then
        printf '%s\n' 'codex/AGENTS.md   in_sync' >"$test_root/status-expected"
        printf '%s\n' 'codex/config.toml in_sync' >>"$test_root/status-expected"
    else
        printf '%s\n' 'copilot/copilot-instructions.md in_sync' >"$test_root/status-expected"
    fi
    assert_output "$test_root/status-expected" "$test_root/status-output"

    # Change exactly one authored provider section, leaving all markers intact.
    # Command substitution removes awk's final newline: rendered targets end
    # at their last marker, and extra outside-section bytes are not attributable.
    changed_runtime=$(awk -v before="$original_line" -v after="$updated_line" \
        '$0 == before { $0 = after } { print }' "$runtime")
    printf '%s' "$changed_runtime" >"$runtime"
    cp "$runtime" "$test_root/$provider-runtime-before-backup"
    head_before_backup=$(git -C "$data_repo" rev-parse HEAD)
    run_provider "$provider" backup >"$test_root/backup-output"
    printf 'committed %s\n' "$(git -C "$data_repo" rev-parse HEAD)" >"$test_root/backup-expected"
    assert_output "$test_root/backup-expected" "$test_root/backup-output"
    [ "$(git -C "$data_repo" log -1 --format=%s)" = "$commit_subject" ] || fail "unexpected backup subject"
    [ "$(git -C "$data_repo" rev-parse HEAD^)" = "$head_before_backup" ] || fail "backup did not create one commit"
    printf '%s\n' "$updated_line" >"$test_root/overlay-expected"
    assert_file_equals "$test_root/overlay-expected" "$data_repo/$overlay_relative"
    printf '%s\n' "$overlay_relative" "$target_relative" >"$test_root/backup-paths-expected"
    git -C "$data_repo" diff-tree --no-commit-id --name-only -r HEAD >"$test_root/backup-paths"
    assert_output "$test_root/backup-paths-expected" "$test_root/backup-paths"
    assert_file_equals "$test_root/$provider-runtime-before-backup" "$runtime"

    # Backup does not rewrite the runtime's last-applied digest. Status must
    # report drift until apply refreshes that block from the new committed target.
    status_exit=0
    run_provider "$provider" status >"$test_root/status-output" || status_exit=$?
    [ "$status_exit" = 2 ] || fail "backup did not leave expected digest drift"
    sed 's/ in_sync$/ different/' "$test_root/status-expected" >"$test_root/status-drift-expected"
    if [ "$provider" = codex ]; then
        printf '%s\n' 'codex/AGENTS.md   different' 'codex/config.toml in_sync' >"$test_root/status-drift-expected"
    fi
    assert_output "$test_root/status-drift-expected" "$test_root/status-output"
    run_provider "$provider" apply >"$test_root/apply-output"
    assert_output "$test_root/apply-expected" "$test_root/apply-output"
    run_provider "$provider" status >"$test_root/status-output"
    assert_output "$test_root/status-expected" "$test_root/status-output"
    cp "$runtime" "$test_root/$provider-runtime-after-backup"

    # Explicit apply replaces drift from committed portable data, not from
    # the preserved historical provider file. It never commits runtime writes.
    printf '\n%s\n' 'macOS apply drift' >>"$runtime"
    head_before_apply=$(git -C "$data_repo" rev-parse HEAD)
    run_provider "$provider" apply >"$test_root/apply-output"
    assert_output "$test_root/apply-expected" "$test_root/apply-output"
    assert_file_equals "$test_root/$provider-runtime-after-backup" "$runtime"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "apply changed HEAD"
    run_provider "$provider" status >"$test_root/status-output"
    assert_output "$test_root/status-expected" "$test_root/status-output"
    run_provider "$provider" backup >"$test_root/backup-again-output"
    printf '%s\n' unchanged >"$test_root/unchanged-expected"
    assert_output "$test_root/unchanged-expected" "$test_root/backup-again-output"
    run_provider "$provider" apply >"$test_root/apply-again-output"
    assert_output "$test_root/unchanged-expected" "$test_root/apply-again-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "idempotent commands changed HEAD"
done
assert_file_equals "$test_root/codex-legacy-original" "$data_repo/environment/providers/codex/AGENTS.md"
assert_file_equals "$test_root/copilot-legacy-original" "$canonical_copilot"
assert_file_equals "$test_root/codex-config-original" "$codex_home/config.toml"
assert_file_equals "$test_root/copilot-config-original" "$copilot_home/config.json"
[ -z "$(git -C "$data_repo" status --porcelain)" ] || fail "lifecycle left repository changes"

# Repository-only render updates both enrolled targets after a shared edit,
# never runtime files, the Git index, or history. No other platform is invented.

cp "$codex_home/AGENTS.md" "$test_root/codex-runtime-before-render"
cp "$codex_home/config.toml" "$test_root/config-before-render"
cp "$runtime_copilot" "$test_root/copilot-runtime-before-render"
head_before_render=$(git -C "$data_repo" rev-parse HEAD)
printf '%s\n' 'Shared approval updated.' >"$shared_source"
"$custom_target" render --repo "$data_repo" >"$test_root/render-output"
printf 'rendered environment/targets/%s/codex/AGENTS.md\nrendered environment/targets/%s/copilot/copilot-instructions.md\n' \
    "$platform" "$platform" >"$test_root/render-expected-output"
assert_output "$test_root/render-expected-output" "$test_root/render-output"
for provider in codex copilot; do
    case "$provider" in
        codex) instruction_file=AGENTS.md; overlay='Codex models updated.' ;;
        copilot) instruction_file=copilot-instructions.md; overlay='Copilot tools updated.' ;;
    esac
    printf '%s\n' \
        '<!-- AEC:SOURCE:SHARED:BEGIN -->' 'Shared approval updated.' '' \
        '<!-- AEC:SOURCE:SHARED:END -->' '' \
        '<!-- AEC:SOURCE:PLATFORM:BEGIN -->' 'Local paths.' '' \
        '<!-- AEC:SOURCE:PLATFORM:END -->' '' \
        '<!-- AEC:SOURCE:PROVIDER:BEGIN -->' "$overlay" '' \
        >"$test_root/render-expected-content"
    printf '%s' '<!-- AEC:SOURCE:PROVIDER:END -->' >>"$test_root/render-expected-content"
    assert_file_equals "$test_root/render-expected-content" "$data_repo/environment/targets/$platform/$provider/$instruction_file"
done
"$custom_target" render --repo "$data_repo" >"$test_root/render-again-output"
printf '%s\n' unchanged >"$test_root/render-again-expected"
assert_output "$test_root/render-again-expected" "$test_root/render-again-output"
[ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_render" ] || \
    fail "render changed repository HEAD"
git -C "$data_repo" diff --cached --quiet || fail "render staged repository changes"
assert_file_equals "$test_root/codex-runtime-before-render" "$codex_home/AGENTS.md"
assert_file_equals "$test_root/config-before-render" "$codex_home/config.toml"
assert_file_equals "$test_root/copilot-runtime-before-render" "$runtime_copilot"
assert_file_equals "$test_root/copilot-config-original" "$copilot_home/config.json"
printf '%s\n' environment/shared/instructions.md \
    "environment/targets/$platform/codex/AGENTS.md" \
    "environment/targets/$platform/copilot/copilot-instructions.md" >"$test_root/render-paths-expected"
git -C "$data_repo" diff --name-only >"$test_root/render-paths"
assert_output "$test_root/render-paths-expected" "$test_root/render-paths"
[ "$(find "$data_repo/environment/targets" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')" = 1 ] || \
    fail "render created an unused platform"

# A small fake binary makes the generated script's all-or-nothing cleanup testable
# without changing the actual personal Codex environment.
fake_source="$test_root/fake-source"
fake_artifact="$test_root/fake-aec"
mkdir -p "$fake_source/scripts" "$fake_source/artifacts/aec-osx-arm64"
cp "$source_installer" "$fake_source/scripts/install-osx-arm64.sh"
printf '%s\n' '#!/bin/sh' \
    'if [ "$1" = "version" ]; then printf "2.0.0-alpha.4\\n"; exit 0; fi' \
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

printf '%s\n' 'macOS installer, AEC lifecycle, and render smoke passed'
