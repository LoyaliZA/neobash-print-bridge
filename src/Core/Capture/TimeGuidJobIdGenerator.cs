namespace Neobash.PrintBridge.Core.Capture;

public sealed class TimeGuidJobIdGenerator : IJobIdGenerator
{
    public string Create()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        return timestamp + "-" + Guid.NewGuid().ToString("N");
    }
}
