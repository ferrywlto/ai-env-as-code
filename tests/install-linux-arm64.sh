#!/bin/sh
# Exercise the Linux installer and generated uninstaller entirely under a
# disposable root. The runner's real home and repository remain untouched.
set -eu

fail()
{
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_installer="$repo_root/scripts/install-linux-arm64.sh"
source_artifact="$repo_root/artifacts/aec-linux-arm64/aec"
[ -x "$source_installer" ] || fail "installer is missing"
[ -x "$source_artifact" ] || fail "Native AOT artifact is missing"

test_root=$(CDPATH= cd -- "$(mktemp -d "${TMPDIR:-/tmp}/aec-linux-installer.XXXXXX")" && pwd)
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

# Use the real AEC binary to create managed state in an isolated Codex home.
# Process-only Git identity variables avoid changing global Git configuration.
codex_home="$test_root/codex-home"
mkdir -p "$codex_home"
printf 'Personal instructions\n' >"$codex_home/AGENTS.md"
printf 'personality = "none"\n' >"$codex_home/config.toml"
data_repo="$test_root/aec-data"
export GIT_AUTHOR_NAME='AEC Installer Test'
export GIT_AUTHOR_EMAIL='aec-installer-test@example.invalid'
export GIT_COMMITTER_NAME='AEC Installer Test'
export GIT_COMMITTER_EMAIL='aec-installer-test@example.invalid'
"$custom_target" init --repo "$data_repo" --codex-home "$codex_home" >/dev/null
[ -f "$codex_home/skills/aec/SKILL.md" ] || fail "AEC skill was not installed"
grep -F '<!-- AEC:BEGIN' "$codex_home/AGENTS.md" >/dev/null || fail "managed block was not installed"
canonical="$data_repo/environment/providers/codex/AGENTS.md"
canonical_hash=$(sha256sum "$canonical")
config_hash=$(sha256sum "$codex_home/config.toml")

"$uninstaller" --codex-home "$codex_home" >/dev/null
[ ! -e "$custom_target" ] || fail "uninstall left the custom executable"
[ ! -e "$uninstaller" ] || fail "uninstall left the helper"
[ -f "$default_target" ] || fail "uninstall removed the older install target"
[ -f "$canonical" ] || fail "uninstall removed canonical data"
[ "$(sha256sum "$canonical")" = "$canonical_hash" ] || fail "uninstall changed canonical data"
[ "$(sha256sum "$codex_home/config.toml")" = "$config_hash" ] || fail "uninstall changed config.toml"
grep -F 'Personal instructions' "$codex_home/AGENTS.md" >/dev/null || fail "uninstall removed personal instructions"
if grep -F '<!-- AEC:BEGIN' "$codex_home/AGENTS.md" >/dev/null; then
  fail "uninstall left the managed block"
fi
[ ! -e "$codex_home/skills/aec/SKILL.md" ] || fail "uninstall left the skill"

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

printf '%s\n' 'Linux installer lifecycle tests passed'
