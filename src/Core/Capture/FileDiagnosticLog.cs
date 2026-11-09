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
            Append(message);
        }
    }

    public void Timing(string operation, TimeSpan elapsed, params (string Key, string Value)[] fields)
    {
        var suffix = fields.Length == 0
            ? ""
            : " " + string.Join(' ', fields.Select(FormatField));
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            Append($"timing operation={operation} elapsedMs={elapsed.TotalMilliseconds:0}{suffix}");
        }
    }

    private void Append(string message) =>
        File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");

    private static string FormatField((string Key, string Value) field)
    {
        var value = field.Value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return $"{field.Key}={value}";
    }
}
