namespace Neobash.PrintBridge.Core.Capture;

public interface IJobWorkspace
{
    string Location { get; }

    Stream CreateExclusive(string fileName);

    void WriteText(string fileName, string content);
}
