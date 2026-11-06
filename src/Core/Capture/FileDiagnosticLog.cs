namespace Neobash.PrintBridge.Core.Capture;

public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileDiagnosticLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("La ruta del registro no es válida.", nameof(path));
        }

        _path = path;
    }

    public void Write(string message)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
    }
}
