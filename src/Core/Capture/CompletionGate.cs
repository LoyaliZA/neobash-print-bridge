namespace Neobash.PrintBridge.Core.Capture;

public sealed class CompletionGate
{
    private int _completed;

    public bool TryEnter() => Interlocked.Exchange(ref _completed, 1) == 0;
}
