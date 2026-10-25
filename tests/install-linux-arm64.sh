#!/bin/sh
# Exercise the Linux installer and generated uninstaller entirely under a
# disposable root. The runner's real home and repository remain untouched.
set -eu

fail()
{
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

assert_file_equals() {
  cmp -s "$1" "$2" || fail "files differ: $2"
}

assert_absent() {
  [ ! -e "$1" ] || fail "expected path to be absent: $1"
}

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_installer="$repo_root/scripts/install-linux-arm64.sh"
source_artifact="$repo_root/artifacts/aec-linux-arm64/aec"
[ -x "$source_installer" ] || fail "installer is missing"
[ -x "$source_artifact" ] || fail "Native AOT artifact is missing"

# Resolve the disposable path before AEC checks for symlink ancestors.
test_root=$(CDPATH= cd -- "$(mktemp -d "${TMPDIR:-/tmp}/aec-linux-installer.XXXXXX")" && pwd -P)
cleanup()
{
  rm -rf -- "$test_root"
}
trap cleanup 0 HUP INT TERM

source_root="$test_root/source"
mkdir -p "$source_root/scripts" "$source_root/artifacts/aec-linux-arm64"
cp "$source_installer" "$source_root/scripts/install-linux-arm64.sh"
cp "$source_artifact" "$source_root/artifacts/aec-linux-arm64/aec"
chmod 0755 "$source_root/scripts/install-linux-arm64.sh" "$source_root/artifacts/aec-linux-arm64/aec"

installer="$source_root/scripts/install-linux-arm64.sh"
uninstaller="$source_root/scripts/uninstall-aec-linux-arm64.sh"
export HOME="$test_root/home"
default_target="$HOME/.local/bin/aec"

if "$installer" --install-dir relative >/dev/null 2>&1; then
  fail "relative install directory was accepted"
fi
mkdir -p "$test_root/redirected"
ln -s "$test_root/redirected" "$test_root/linked-parent"
if "$installer" --install-dir "$test_root/linked-parent/bin" >/dev/null 2>&1; then
  fail "linked install-directory ancestor was accepted"
fi
[ ! -e "$test_root/redirected/bin/aec" ] || fail "linked path received a binary"

"$installer" >"$test_root/initial-output"
[ -x "$default_target" ] || fail "default executable was not installed"
[ -x "$uninstaller" ] || fail "uninstaller was not generated"
cmp -s "$source_artifact" "$default_target" || fail "installed executable differs"
grep -F "installed $default_target" "$test_root/initial-output" >/dev/null || fail "binary result was not reported"
grep -F "installed $uninstaller" "$test_root/initial-output" >/dev/null || fail "helper result was not reported"

"$installer" >"$test_root/reinstall-output"
grep -F "unchanged $default_target" "$test_root/reinstall-output" >/dev/null || fail "reinstall was not idempotent"

custom_dir="$test_root/custom install/bin"
custom_target="$custom_dir/aec"
"$installer" --install-dir "$custom_dir" >"$test_root/custom-output" 2>"$test_root/custom-error"
[ -x "$custom_target" ] || fail "custom executable was not installed"
[ -x "$default_target" ] || fail "custom install removed the old executable"
grep -F "warning: custom AEC install directory: $custom_dir" "$test_root/custom-error" >/dev/null || fail "custom path warning was missing"

# A copied helper and a changed executable must fail before runtime cleanup.
copied_uninstaller="$test_root/copied-uninstaller.sh"
cp "$uninstaller" "$copied_uninstaller"
chmod 0755 "$copied_uninstaller"
if "$copied_uninstaller" --codex-home "$test_root/missing-home" >/dev/null 2>&1; then
  fail "copied uninstaller was accepted"
fi
[ -x "$custom_target" ] || fail "copied helper removed the executable"

if "$uninstaller" --codex-home relative >/dev/null 2>&1; then
  fail "relative Codex home was accepted"
fi
[ -x "$custom_target" ] || fail "invalid arguments removed the executable"
[ -x "$uninstaller" ] || fail "invalid arguments removed the helper"

printf 'tamper' >>"$custom_target"
if "$uninstaller" --codex-home "$test_root/missing-home" >/dev/null 2>&1; then
  fail "changed executable was accepted"
fi
[ -x "$uninstaller" ] || fail "hash failure removed the helper"
"$installer" --install-dir "$custom_dir" >/dev/null 2>&1
cmp -s "$source_artifact" "$custom_target" || fail "reinstall did not repair the executable"
if "$uninstaller" --codex-home "$test_root/missing-home" >/dev/null 2>&1; then
  fail "failed runtime cleanup was accepted"
fi
[ -x "$custom_target" ] || fail "failed runtime cleanup removed the executable"
[ -x "$uninstaller" ] || fail "failed runtime cleanup removed the helper"

# A private Git configuration keeps identity, signing, and newline settings
# independent of the runner's or developer's configuration.
git_config="$test_root/gitconfig"
printf '%s\n' \
    '[user]' \
    '    name = AEC Linux Smoke' \
    '    email = aec-linux-smoke@example.invalid' \
    '[commit]' \
    '    gpgSign = false' \
    '[core]' \
    '    autocrlf = false' >"$git_config"
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
[ -f "$codex_home/skills/aec/SKILL.md" ] || fail "AEC skill was not installed"
grep -F '<!-- AEC:BEGIN' "$codex_home/AGENTS.md" >/dev/null || fail "managed block was not installed"

copilot_home="$test_root/copilot-home"
runtime_copilot="$copilot_home/copilot-instructions.md"
canonical_copilot="$data_repo/environment/providers/copilot/copilot-instructions.md"
copilot_skill="$copilot_home/skills/aec/SKILL.md"
mkdir -p "$copilot_home"
printf '%s\n' 'Shared approval.' 'Local paths.' 'Copilot tools.' >"$runtime_copilot"
"$custom_target" init --repo "$data_repo" --provider=copilot --copilot-home "$copilot_home" >/dev/null

[ -f "$canonical_copilot" ] || fail "canonical Copilot instructions were not created"
[ -f "$copilot_skill" ] || fail "Copilot AEC skill was not installed"
assert_file_equals "$canonical_copilot" "$runtime_copilot"
grep -F '<!-- AEC:COPILOT:BEGIN' "$runtime_copilot" >/dev/null || \
    fail "Copilot managed block was not installed"
[ ! -e "$copilot_home/config.json" ] || fail "Copilot initialization created unmanaged config.json"
# Subsequent commands must preserve a local config that AEC does not manage.
printf '%s\n' '{"localSetting":true}' >"$copilot_home/config.json"
cp "$copilot_home/config.json" "$test_root/copilot-config-original"
# Ordinary init retains the historical canonical files. Commit Copilot's
# baseline before authoring the reviewed split; public 2.0 commands require
# committed portable targets rather than silently using these legacy files.
git -C "$data_repo" add -- environment/providers/copilot/copilot-instructions.md
git -C "$data_repo" commit -m 'Record smoke Copilot baseline' >/dev/null
cp "$codex_home/AGENTS.md" "$test_root/codex-legacy-original"
cp "$runtime_copilot" "$test_root/copilot-legacy-original"
platform=linux-arm64
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
        *) fail "unsupported provider: $1" ;;
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
    assert_file_equals "$test_root/enroll-expected" "$test_root/enroll-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_enroll" ] || fail "enrollment committed"
    git -C "$data_repo" diff --cached --quiet || fail "enrollment staged changes"
    assert_file_equals "$test_root/codex-legacy-original" "$codex_home/AGENTS.md"
    assert_file_equals "$test_root/copilot-legacy-original" "$runtime_copilot"
    if [ "$provider" = codex ]; then
        assert_absent "$data_repo/environment/targets/$platform/copilot"
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
    assert_file_equals "$test_root/apply-expected" "$test_root/apply-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "initial apply changed HEAD"
    run_provider "$provider" status >"$test_root/status-output"
    if [ "$provider" = codex ]; then
        printf '%s\n' 'codex/AGENTS.md   in_sync' >"$test_root/status-expected"
        printf '%s\n' 'codex/config.toml in_sync' >>"$test_root/status-expected"
    else
        printf '%s\n' 'copilot/copilot-instructions.md in_sync' >"$test_root/status-expected"
    fi
    assert_file_equals "$test_root/status-expected" "$test_root/status-output"

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
    assert_file_equals "$test_root/backup-expected" "$test_root/backup-output"
    [ "$(git -C "$data_repo" log -1 --format=%s)" = "$commit_subject" ] || fail "unexpected backup subject"
    [ "$(git -C "$data_repo" rev-parse HEAD^)" = "$head_before_backup" ] || fail "backup did not create one commit"
    printf '%s\n' "$updated_line" >"$test_root/overlay-expected"
    assert_file_equals "$test_root/overlay-expected" "$data_repo/$overlay_relative"
    printf '%s\n' "$overlay_relative" "$target_relative" >"$test_root/backup-paths-expected"
    git -C "$data_repo" diff-tree --no-commit-id --name-only -r HEAD >"$test_root/backup-paths"
    assert_file_equals "$test_root/backup-paths-expected" "$test_root/backup-paths"
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
    assert_file_equals "$test_root/status-drift-expected" "$test_root/status-output"
    run_provider "$provider" apply >"$test_root/apply-output"
    assert_file_equals "$test_root/apply-expected" "$test_root/apply-output"
    run_provider "$provider" status >"$test_root/status-output"
    assert_file_equals "$test_root/status-expected" "$test_root/status-output"
    cp "$runtime" "$test_root/$provider-runtime-after-backup"

    # Explicit apply replaces drift from committed portable data, not from
    # the preserved historical provider file. It never commits runtime writes.
    printf '\n%s\n' 'Linux apply drift' >>"$runtime"
    head_before_apply=$(git -C "$data_repo" rev-parse HEAD)
    run_provider "$provider" apply >"$test_root/apply-output"
    assert_file_equals "$test_root/apply-expected" "$test_root/apply-output"
    assert_file_equals "$test_root/$provider-runtime-after-backup" "$runtime"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "apply changed HEAD"
    run_provider "$provider" status >"$test_root/status-output"
    assert_file_equals "$test_root/status-expected" "$test_root/status-output"
    run_provider "$provider" backup >"$test_root/backup-again-output"
    printf '%s\n' unchanged >"$test_root/unchanged-expected"
    assert_file_equals "$test_root/unchanged-expected" "$test_root/backup-again-output"
    run_provider "$provider" apply >"$test_root/apply-again-output"
    assert_file_equals "$test_root/unchanged-expected" "$test_root/apply-again-output"
    [ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_apply" ] || fail "idempotent commands changed HEAD"
done
assert_file_equals "$test_root/codex-legacy-original" "$data_repo/environment/providers/codex/AGENTS.md"
assert_file_equals "$test_root/copilot-legacy-original" "$canonical_copilot"
assert_file_equals "$test_root/codex-config-original" "$codex_home/config.toml"
assert_file_equals "$test_root/copilot-config-original" "$copilot_home/config.json"
[ -z "$(git -C "$data_repo" status --porcelain)" ] || fail "lifecycle left repository changes"

# Repository-only render updates both enrolled targets after a shared edit,
# never runtime files, the Git index, or history. No other platform is invented.

cp "$data_repo/environment/targets/$platform/codex/AGENTS.md" "$test_root/codex-authored-before-render"
cp "$codex_home/AGENTS.md" "$test_root/codex-runtime-before-render"
cp "$codex_home/config.toml" "$test_root/config-before-render"
cp "$runtime_copilot" "$test_root/copilot-runtime-before-render"
head_before_render=$(git -C "$data_repo" rev-parse HEAD)
printf '%s\n' 'Shared approval updated.' >"$shared_source"
"$custom_target" render --repo "$data_repo" >"$test_root/render-output"
printf 'rendered environment/targets/%s/codex/AGENTS.md\nrendered environment/targets/%s/copilot/copilot-instructions.md\n' \
    "$platform" "$platform" >"$test_root/render-expected-output"
assert_file_equals "$test_root/render-expected-output" "$test_root/render-output"
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
assert_file_equals "$test_root/render-again-expected" "$test_root/render-again-output"
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
assert_file_equals "$test_root/render-paths-expected" "$test_root/render-paths"
[ "$(find "$data_repo/environment/targets" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')" = 1 ] || \
    fail "render created an unused platform"

# Removing Codex integration preserves the last applied authored sections,
# not the newer, deliberately unapplied render. Snapshot all data files (also
# dirty targets) and the other harness before invoking the real uninstaller.
snapshot_data_files() {
    # Keep find outside a pipeline so a failed hash cannot be hidden by sort.
    find "$data_repo" -path "$data_repo/.git" -prune -o -type f -exec sha256sum {} + \
        >"$test_root/data-hashes-unsorted"
    LC_ALL=C sort "$test_root/data-hashes-unsorted"
}
snapshot_data_files >"$test_root/data-before-uninstall"
cp "$copilot_home/skills/aec/SKILL.md" "$test_root/copilot-skill-before-uninstall"
"$uninstaller" --codex-home "$codex_home" >/dev/null
assert_absent "$custom_target"
assert_absent "$uninstaller"
[ -f "$default_target" ] || fail "uninstall removed the older install target"
assert_file_equals "$test_root/codex-authored-before-render" "$codex_home/AGENTS.md"
assert_file_equals "$test_root/codex-config-original" "$codex_home/config.toml"
if grep -F '<!-- AEC:BEGIN' "$codex_home/AGENTS.md" >/dev/null; then
    fail "uninstall left the managed block"
fi
assert_absent "$codex_home/skills/aec/SKILL.md"
snapshot_data_files >"$test_root/data-after-uninstall"
assert_file_equals "$test_root/data-before-uninstall" "$test_root/data-after-uninstall"
[ "$(git -C "$data_repo" rev-parse HEAD)" = "$head_before_render" ] || fail "uninstall changed data HEAD"
git -C "$data_repo" diff --cached --quiet || fail "uninstall staged data changes"
git -C "$data_repo" diff --name-only >"$test_root/data-diff-after-uninstall"
assert_file_equals "$test_root/render-paths-expected" "$test_root/data-diff-after-uninstall"
assert_file_equals "$test_root/copilot-runtime-before-render" "$runtime_copilot"
assert_file_equals "$test_root/copilot-config-original" "$copilot_home/config.json"
assert_file_equals "$test_root/copilot-skill-before-uninstall" "$copilot_home/skills/aec/SKILL.md"

# Preserve a personal file at the generated-helper path and write no binary.
conflict_root="$test_root/conflict-source"
mkdir -p "$conflict_root/scripts" "$conflict_root/artifacts/aec-linux-arm64"
cp "$source_installer" "$conflict_root/scripts/install-linux-arm64.sh"
cp "$source_artifact" "$conflict_root/artifacts/aec-linux-arm64/aec"
chmod 0755 "$conflict_root/scripts/install-linux-arm64.sh" "$conflict_root/artifacts/aec-linux-arm64/aec"
printf 'personal script\n' >"$conflict_root/scripts/uninstall-aec-linux-arm64.sh"
if "$conflict_root/scripts/install-linux-arm64.sh" --install-dir "$test_root/conflict/bin" >/dev/null 2>"$test_root/conflict-error"; then
  fail "installer overwrote a personal helper"
fi
[ ! -e "$test_root/conflict/bin/aec" ] || fail "conflicting install wrote a binary"
grep -F 'Refusing to overwrite non-AEC uninstaller' "$test_root/conflict-error" >/dev/null || fail "conflict was not explained"

printf '%s\n' 'Linux installer, AEC lifecycle, and render smoke passed'
