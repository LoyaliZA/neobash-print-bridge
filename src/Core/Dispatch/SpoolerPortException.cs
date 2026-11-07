namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class SpoolerPortException : Exception
{
    public SpoolerPortException(bool started, string message)
        : base(message)
    {
        Started = started;
    }

    public bool Started { get; }
}
