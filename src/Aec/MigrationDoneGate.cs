namespace Aec;

internal enum MigrationDoneResult
{
    Done,
    Cancelled
}

// Confirmation only. The caller must freshly validate edited sources before
// committing or deploying; DONE is not proof that migration has completed.
internal static class MigrationDoneGate
{
    public static MigrationDoneResult Wait(TextReader input, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (cancellationToken.IsCancellationRequested) return MigrationDoneResult.Cancelled;

        output.WriteLine("""
            Finish editing the prepared sections, then type exactly DONE.
            DONE requests validation; it does not complete migration.
            EOF or cancellation stops without completing migration.
            """);
        while (!cancellationToken.IsCancellationRequested)
        {
            // Synchronous input may stay blocked until a line arrives. Check
            // again afterward so cancellation wins even if that line is DONE.
            var answer = input.ReadLine();
            if (answer is null) return MigrationDoneResult.Cancelled;
            if (cancellationToken.IsCancellationRequested) return MigrationDoneResult.Cancelled;
            if (string.Equals(answer, "DONE", StringComparison.Ordinal)) return MigrationDoneResult.Done;
            output.WriteLine("Not confirmed. Enter exactly DONE when your sections are ready.");
        }
        return MigrationDoneResult.Cancelled;
    }
}
