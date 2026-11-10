namespace Cabinet.Cli;

internal static class Interrupt
{
    public static void Cancels(string what, Action<CancellationToken> work) =>
        Cancels(what, token =>
        {
            work(token);
            return true;
        });

    public static T Cancels<T>(string what, Func<CancellationToken, T> work)
    {
        using var cancellation = new CancellationTokenSource();

        void Stop(object? sender, ConsoleCancelEventArgs interrupted)
        {
            interrupted.Cancel = !cancellation.IsCancellationRequested;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Stop;

        try
        {
            return work(cancellation.Token);
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{what} was cancelled");
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
        }
    }
}
