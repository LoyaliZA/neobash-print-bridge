namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class SpoolerPortException : Exception
{
    public SpoolerPortException(bool started, string message, bool requiresClippingConfirmation = false)
        : base(message)
    {
        Started = started;
        RequiresClippingConfirmation = !started && requiresClippingConfirmation;
    }

    public bool Started { get; }

    public bool RequiresClippingConfirmation { get; }
}
