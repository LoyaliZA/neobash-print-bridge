using System.Text.Json;
using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class FileJobRecordStore : IJobRecordStore
{
    private readonly string _root;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, JobRecord> _cache = new(StringComparer.Ordinal);
    private readonly List<string> _readWarnings = [];
    private bool _cacheLoaded;

    public FileJobRecordStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("La ruta de almacenamiento no es válida.", nameof(root));
        }

        _root = Path.Combine(Path.GetFullPath(root), "jobs");
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<string> ReadWarnings
    {
        get
        {
            lock (_gate)
            {
                return _readWarnings.ToArray();
            }
        }
    }

    public void RebuildFromDisk()
    {
        lock (_gate)
        {
            _cache.Clear();
            _readWarnings.Clear();
            _cacheLoaded = false;
        }
    }

    public bool TryBegin(PrintJob job, string queueName)
    {
        ArgumentNullException.ThrowIfNull(job);
        JobIdRules.EnsureSafe(job.Id);
        lock (_gate)
        {
            var path = PathFor(job.Id);
            if (File.Exists(path))
            {
                return false;
            }

            Write(path, new JobRecord
            {
                Id = job.Id,
                MediaType = job.MediaType,
                LocalPath = job.LocalPath,
                DestinationId = job.DestinationId,
                PaperName = job.PaperName,
                Options = job.Options,
                QueueName = queueName,
                Phase = JobPhase.Received,
                ReceivedAt = DateTimeOffset.UtcNow,
            });
            return true;
        }
    }

    public void Update(string id, JobPhase phase, string? detail)
    {
        lock (_gate)
        {
            var record = Require(id);
            record.Phase = phase;
            record.Detail = detail;
            Write(PathFor(id), record);
        }
    }

    public void RecordPhysicalConfirmation(string id, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("La confirmación física necesita una nota.", nameof(note));
        }

        lock (_gate)
        {
            var record = Require(id);
            if (record.Phase != JobPhase.SubmittedToSpooler)
            {
                throw new InvalidOperationException("La confirmación física solo se anota cuando el spooler ya aceptó el trabajo.");
            }

            record.PhysicalConfirmation = note.Trim();
            Write(PathFor(id), record);
        }
    }

    public JobRecord? Find(string id)
    {
        lock (_gate)
        {
            // The list cache is only a display snapshot. Another Desktop instance
            // may have committed a final phase or a physical confirmation since.
            var path = PathFor(id);
            return File.Exists(path) ? TryRead(path) : null;
        }
    }

    public IReadOnlyList<JobRecord> List()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _cache.Values.Select(Clone).OrderBy(record => record.ReceivedAt).ToList();
        }
    }

    public IReadOnlyList<string> ListIds()
    {
        lock (_gate)
        {
            return Directory.EnumerateFiles(_root, "*.json")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .ToList();
        }
    }

    private void EnsureLoaded()
    {
        if (_cacheLoaded)
        {
            return;
        }

        _cache.Clear();
        _readWarnings.Clear();
        foreach (var path in Directory.EnumerateFiles(_root, "*.json"))
        {
            var record = TryRead(path);
            if (record is not null)
            {
                _cache[record.Id] = Clone(record);
            }
        }

        _cacheLoaded = true;
    }

    private JobRecord Require(string id)
    {
        JobIdRules.EnsureSafe(id);
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("El trabajo no está registrado.");
        }

        return TryRead(path) ?? throw new InvalidOperationException("El historial del trabajo no se puede leer.");
    }

    private string PathFor(string id)
    {
        JobIdRules.EnsureSafe(id);
        var path = Path.GetFullPath(Path.Combine(_root, id + ".json"));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El trabajo quedaría fuera del historial.");
        }

        return path;
    }

    private JobRecord? TryRead(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            _cache.Remove(id);
            var warning = $"No se pudo leer el historial «{id}».";
            if (!_readWarnings.Contains(warning, StringComparer.Ordinal))
            {
                _readWarnings.Add(warning);
            }

            return null;
        }
    }

    private static JobRecord Read(string path)
    {
        var record = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(path), DispatchJson.Options);
        return record ?? throw new InvalidOperationException("El historial del trabajo no se puede leer.");
    }

    private void Write(string path, JobRecord record)
    {
        var json = JsonSerializer.Serialize(record, DispatchJson.Options);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
        _cache[record.Id] = Clone(record);
    }

    private static JobRecord Clone(JobRecord record) => new()
    {
        Id = record.Id,
        MediaType = record.MediaType,
        LocalPath = record.LocalPath,
        DestinationId = record.DestinationId,
        PaperName = record.PaperName,
        Options = record.Options,
        QueueName = record.QueueName,
        Phase = record.Phase,
        Detail = record.Detail,
        PhysicalConfirmation = record.PhysicalConfirmation,
        ReceivedAt = record.ReceivedAt,
    };
}
