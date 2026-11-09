namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record DispatchResult(JobPhase Phase, string Detail, bool RequiresClippingConfirmation = false);
