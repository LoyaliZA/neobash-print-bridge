namespace Neobash.PrintBridge.Core.Capture;

public interface IDiagnosticLog
{
    void Write(string message);

    void Timing(string operation, TimeSpan elapsed, params (string Key, string Value)[] fields);
}
