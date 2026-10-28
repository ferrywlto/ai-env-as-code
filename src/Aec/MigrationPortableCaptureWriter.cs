namespace Aec;

// A bounded migration adapter, not a second backup implementation. Existing
// provider writers own source replacement, affected-target rendering and exact
// commit scope. Multi-provider capture needs a later combined baseline.
internal static class MigrationPortableCaptureWriter
{
    public static string? WriteAndCommit(MigrationPortableCapturePreview reviewed)
    {
        var fresh = RecheckPreview(reviewed);
        var repository = fresh.Preparation.Preflight.Repository;

        var codexChanged = fresh.Codex?.HasChanges == true;
        var copilotChanged = fresh.Copilot?.Instructions.Section is not null;
        if (codexChanged && copilotChanged)
        {
            // Even identical shared edits cannot use two old previews in
            // sequence: the first commit changes the second target baseline.
            throw new InvalidOperationException("Both portable providers have changes; combined migration capture is not implemented. No files were written.");
        }

        var parent = fresh.Preparation.Preflight.Commit;
        var runtimes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var harness in fresh.Preparation.Harnesses)
        {
            var previewRuntime = harness.Provider == "codex"
                ? fresh.Codex?.Snapshot.Runtime : fresh.Copilot?.Snapshot.Runtime;
            runtimes.Add(harness.RuntimePath, previewRuntime ?? ReadRuntime(harness.RuntimePath));
            if (harness.Provider == "codex")
            {
                var path = Path.Combine(harness.Home, "config.toml");
                runtimes.Add(path, fresh.Codex?.RuntimeConfig ?? ReadRuntime(path));
            }
        }
        VerifyState(parent);
        EnsureEmptyIndex(repository);
        if (!codexChanged && !copilotChanged) return null;

        try
        {
            // Dispatch only fresh derived proposals, never mutable proposal
            // fields supplied by the caller. Do not reread migration previews
            // afterward: runtime digests stay unchanged until an approved apply.
            var commit = codexChanged
                ? PortableCodexBackupWriter.WriteAndCommit(fresh.Codex!)
                : PortableCopilotBackupWriter.WriteAndCommit(fresh.Copilot!);
            var parents = GitProcess.RunRequired(repository, "Could not verify portable capture parent",
                "--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Output.Trim();
            if (parents != $"{commit} {parent}")
            {
                throw new InvalidOperationException("Portable capture did not create exactly one child of the reviewed commit.");
            }
            VerifyState(commit);
            EnsureEmptyIndex(repository);
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new IOException("Portable migration capture did not complete. Working files, staged changes or a new commit may remain. Inspect HEAD, index and working files before retrying; no rollback, push or runtime apply was attempted.", exception);
        }

        void VerifyState(string expectedHead)
        {
            AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
            AecApplication.EnsureNoLinksInExistingPath(Path.Combine(repository, ".git"), "Git metadata path");
            if (ApplyCommand.ResolveHeadCommit(repository) != expectedHead ||
                MigrationLegacyCapturePreview.ReadBranch(repository) != fresh.Branch ||
                AecApplication.ReadOptionalTextFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath), "Repository format marker") is not null ||
                !MigrationHarnessSelection.Read(fresh.Preparation.Preflight with { Commit = expectedHead })
                    .SequenceEqual(fresh.Preparation.Harnesses) ||
                runtimes.Any(file => !SameBytes(file.Value, ReadRuntime(file.Key))))
            {
                throw new InvalidOperationException("Migration HEAD, branch, format, enrollment or runtime changed during portable capture.");
            }
        }
    }

    // Both writing boundaries require the same fresh, read-only validation and
    // empty index. Derived proposals are deliberately recomputed, not trusted.
    internal static MigrationPortableCapturePreview RecheckPreview(MigrationPortableCapturePreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var repository = reviewed.Preparation.Preflight.Repository;
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        EnsureEmptyIndex(repository);
        var fresh = MigrationPortableCapturePreview.Read(reviewed.Preparation);
        if (reviewed.Branch != fresh.Branch ||
            !SameInputs(reviewed.Codex?.Snapshot, fresh.Codex?.Snapshot) ||
            !SameInputs(reviewed.Copilot?.Snapshot, fresh.Copilot?.Snapshot) ||
            reviewed.Codex is not null &&
            (!SameBytes(reviewed.Codex.CanonicalConfig, fresh.Codex!.CanonicalConfig) ||
             !SameBytes(reviewed.Codex.RuntimeConfig, fresh.Codex.RuntimeConfig)))
        {
            throw new InvalidOperationException("Portable migration capture inputs changed after review; no files were written.");
        }

        return fresh;
    }

    private static bool SameInputs(PortableRuntimeSnapshot? reviewed, PortableRuntimeSnapshot? fresh)
    {
        if (reviewed is null || fresh is null) return reviewed is null && fresh is null;
        return reviewed.Repository == fresh.Repository && reviewed.Provider == fresh.Provider &&
            reviewed.Platform == fresh.Platform && reviewed.RuntimePath == fresh.RuntimePath &&
            reviewed.Commit == fresh.Commit && reviewed.BlockVersion == fresh.BlockVersion &&
            SameBytes(reviewed.Target, fresh.Target) && SameBytes(reviewed.Runtime, fresh.Runtime);
    }

    private static bool SameBytes(byte[]? first, byte[]? second) =>
        first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);

    private static byte[]? ReadRuntime(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Runtime path");
        return AecApplication.ReadOptionalTextFile(path, "Runtime file");
    }

    internal static void EnsureEmptyIndex(string repository)
    {
        if (GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code", "--no-ext-diff", "--no-textconv").ExitCode != 0)
        {
            throw new InvalidOperationException("Portable migration capture requires an empty staged diff; no automatic unstaging was performed.");
        }
    }
}
