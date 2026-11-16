if (args.Length == 0 || args[0] != "migrate")
    return Aec.AecApplication.Run(args, Console.Out, Console.Error);

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelMigration = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelMigration;
try
{
    return Aec.AecApplication.Run(args, Console.In, Console.Out, Console.Error, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelMigration;
}
