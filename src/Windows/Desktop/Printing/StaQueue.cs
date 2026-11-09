namespace Neobash.PrintBridge.Desktop;

public sealed class StaQueue : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public StaQueue()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "NPB catalog STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        });
        done.Task.GetAwaiter().GetResult();
    }

    public T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        T? result = default;
        Run(() => result = action());
        return result!;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
    }

    private void Run()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }
}
