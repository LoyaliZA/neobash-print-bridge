using System.Text.Json;
using Microsoft.Data.Sqlite;
using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Lan;

public enum CommitKind
{
    Created,
    Replay,
    Conflict,
    RateLimited,
    Unavailable,
}

public sealed record CommitOutcome(
    CommitKind Kind,
    string? JobId,
    JobPhase? Phase,
    string? ContentSha256,
    string? DestinationId);

public sealed record RecoveryReport(int TemporaryFilesDeleted, int OrphansDeleted, int MarkedUncertain);

public sealed class AgentDatabase : IDisposable, IJobRecordStore, IDestinationStore
{
    public const long MaxBodyBytes = 32L * 1024 * 1024;

    public const int MaxAcceptsPerWindow = 30;

    public static readonly TimeSpan AcceptWindow = TimeSpan.FromMinutes(10);

    private readonly string _incoming;
    private readonly string _documents;
    private readonly string _databasePath;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, byte> _armed = new(StringComparer.Ordinal);

    public AgentDatabase(string dataDirectory)
    {
        var root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(root);
        _incoming = Path.Combine(root, "incoming");
        _documents = Path.Combine(root, "documents");
        Directory.CreateDirectory(_incoming);
        Directory.CreateDirectory(_documents);
        _databasePath = Path.Combine(root, "agent.sqlite");
        DataDirectory = root;
        using var connection = Open();
        CreateSchema(connection);
    }

    public string DataDirectory { get; }

    public string IncomingDirectory => _incoming;

    public string DocumentsDirectory => _documents;

    public string DatabasePath => _databasePath;

    public RecoveryReport Recover()
    {
        int temps;
        int orphans;
        lock (_gate)
        {
            temps = DeleteFiles(_incoming);
            var referenced = ReferencedPaths();
            orphans = 0;
            foreach (var file in Directory.EnumerateFiles(_documents))
            {
                if (!referenced.Contains(Path.GetFullPath(file)))
                {
                    File.Delete(file);
                    orphans++;
                }
            }
        }

        var interrupted = List().Count(record => record.Phase is JobPhase.Received or JobPhase.Prepared);
        JobRecovery.MarkInterruptedAsUncertain(this);
        if (interrupted > 0 || temps > 0 || orphans > 0)
        {
            Audit("recuperacion", null, null, null, null, null, nameof(JobPhase.Uncertain), interrupted.ToString());
        }

        return new RecoveryReport(temps, orphans, interrupted);
    }

    public void EnsurePublished(PublishedDestination destination)
    {
        InImmediate(connection => Execute(
            connection,
            """
            INSERT INTO published_destinations (destination_id, display_name, queue_name, paper_name)
            VALUES ($id, $name, $queue, $paper)
            ON CONFLICT(destination_id) DO UPDATE SET
                display_name = excluded.display_name,
                queue_name = excluded.queue_name,
                paper_name = excluded.paper_name
            """,
            ("$id", destination.Id),
            ("$name", destination.DisplayName),
            ("$queue", destination.QueueName),
            ("$paper", destination.PaperName)));
    }

    public void Grant(string deviceId, string destinationId)
    {
        InImmediate(connection => Execute(
            connection,
            """
            INSERT INTO destination_grants (device_id, destination_id) VALUES ($device, $destination)
            ON CONFLICT(device_id, destination_id) DO NOTHING
            """,
            ("$device", deviceId),
            ("$destination", destinationId)));
    }

    public void SyncPublished(PublishedDestination? destination)
    {
        InImmediate(connection =>
        {
            if (destination is null)
            {
                Execute(connection, "DELETE FROM destination_grants");
                Execute(connection, "DELETE FROM published_destinations");
                return;
            }

            Execute(connection, "DELETE FROM destination_grants WHERE destination_id <> $id", ("$id", destination.Id));
            Execute(connection, "DELETE FROM published_destinations WHERE destination_id <> $id", ("$id", destination.Id));
            Execute(
                connection,
                """
                INSERT INTO published_destinations (destination_id, display_name, queue_name, paper_name)
                VALUES ($id, $name, $queue, $paper)
                ON CONFLICT(destination_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    queue_name = excluded.queue_name,
                    paper_name = excluded.paper_name
                """,
                ("$id", destination.Id),
                ("$name", destination.DisplayName),
                ("$queue", destination.QueueName),
                ("$paper", destination.PaperName));
        });
    }

    public PublishedDestination? FindPublished(string destinationId)
    {
        lock (_gate)
        {
            using var connection = Open();
            return ReadPublished(connection, destinationId);
        }
    }

    public bool IsGranted(string deviceId, string destinationId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, """
                SELECT 1 FROM destination_grants
                WHERE device_id = $device AND destination_id = $destination
                """);
            command.Parameters.AddWithValue("$device", deviceId);
            command.Parameters.AddWithValue("$destination", destinationId);
            return command.ExecuteScalar() is not null;
        }
    }

    public IReadOnlyList<PublishedDestination> ListGranted(string deviceId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, """
                SELECT p.destination_id, p.display_name, p.queue_name, p.paper_name
                FROM published_destinations p
                INNER JOIN destination_grants g ON g.destination_id = p.destination_id
                WHERE g.device_id = $device
                ORDER BY p.display_name
                """);
            command.Parameters.AddWithValue("$device", deviceId);
            using var reader = command.ExecuteReader();
            var list = new List<PublishedDestination>();
            while (reader.Read())
            {
                list.Add(new PublishedDestination(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }

            return list;
        }
    }

    public string NewTempPath()
    {
        var path = Path.Combine(_incoming, "t" + Guid.NewGuid().ToString("N") + ".partial");
        return path;
    }

    public string Publish(string tempPath, string jobId)
    {
        var finalPath = SafeDocumentPath(jobId);
        File.Move(tempPath, finalPath);
        return finalPath;
    }

    public void DeleteIfUnreferenced(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        if (ReferencedPaths().Contains(full))
        {
            return;
        }

        File.Delete(full);
    }

    public void DeleteFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public int CountRecentAccepts(string deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            using var connection = Open();
            return CountRecentAccepts(connection, deviceId, now);
        }
    }

    public CommitOutcome TryCommit(
        string deviceId,
        string sourceId,
        string contentSha256,
        string destinationId,
        string jobId,
        string localPath,
        string mediaType,
        string queueName,
        string paperName,
        PrintOptions options,
        DateTimeOffset now)
    {
        return InImmediate(connection =>
        {
            var existing = ReadIdempotency(connection, deviceId, sourceId);
            if (existing is not null)
            {
                var same = string.Equals(existing.Value.Hash, contentSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.Value.DestinationId, destinationId, StringComparison.Ordinal);
                var phase = ReadPhase(connection, existing.Value.JobId);
                return same
                    ? new CommitOutcome(CommitKind.Replay, existing.Value.JobId, phase, existing.Value.Hash, existing.Value.DestinationId)
                    : new CommitOutcome(CommitKind.Conflict, existing.Value.JobId, phase, existing.Value.Hash, existing.Value.DestinationId);
            }

            if (ReadPublished(connection, destinationId) is null || !GrantExists(connection, deviceId, destinationId))
            {
                return new CommitOutcome(CommitKind.Unavailable, null, null, null, null);
            }

            if (CountRecentAccepts(connection, deviceId, now) >= MaxAcceptsPerWindow)
            {
                return new CommitOutcome(CommitKind.RateLimited, null, null, null, null);
            }

            Execute(
                connection,
                """
                INSERT INTO idempotency (device_id, source_id, content_sha256, destination_id, job_id)
                VALUES ($device, $source, $hash, $destination, $job)
                """,
                ("$device", deviceId),
                ("$source", sourceId),
                ("$hash", contentSha256),
                ("$destination", destinationId),
                ("$job", jobId));
            Execute(
                connection,
                """
                INSERT INTO jobs (
                    job_id, media_type, local_path, destination_id, queue_name, paper_name, options_json,
                    phase, detail, physical_confirmation, received_at)
                VALUES (
                    $job, $media, $path, $destination, $queue, $paper, $options,
                    $phase, $detail, NULL, $received)
                """,
                ("$job", jobId),
                ("$media", mediaType),
                ("$path", localPath),
                ("$destination", destinationId),
                ("$queue", queueName),
                ("$paper", paperName),
                ("$options", JsonSerializer.Serialize(options)),
                ("$phase", nameof(JobPhase.Received)),
                ("$detail", "La transferencia quedó aceptada."),
                ("$received", now.ToString("O")));
            Execute(
                connection,
                "INSERT INTO acceptance_times (device_id, accepted_at) VALUES ($device, $at)",
                ("$device", deviceId),
                ("$at", now.ToString("O")));
            return new CommitOutcome(CommitKind.Created, jobId, JobPhase.Received, contentSha256, destinationId);
        });
    }

    public void ArmDispatch(string jobId)
    {
        lock (_gate)
        {
            _armed[jobId] = 1;
        }
    }

    public void IssueCode(string codeHash, string fingerprint, string expectedHostName, DateTimeOffset expiresAt)
    {
        InImmediate(connection => Execute(
            connection,
            """
            INSERT INTO pairing_codes (code_hash, fingerprint, expected_host_name, expires_at, used_at)
            VALUES ($hash, $fingerprint, $name, $expires, NULL)
            """,
            ("$hash", codeHash),
            ("$fingerprint", fingerprint),
            ("$name", expectedHostName),
            ("$expires", expiresAt.ToString("O"))));
    }

    public CodeConsumption ConsumeCode(string codeHash, string presentedFingerprint, DateTimeOffset now)
    {
        return InImmediate(connection =>
        {
            using var command = Command(connection, """
                SELECT fingerprint, expected_host_name, expires_at, used_at
                FROM pairing_codes WHERE code_hash = $hash
                """);
            command.Parameters.AddWithValue("$hash", codeHash);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return new CodeConsumption(CodeStatus.Unknown, null, null);
            }

            var fingerprint = reader.GetString(0);
            var hostName = reader.GetString(1);
            var expires = DateTimeOffset.Parse(reader.GetString(2));
            var used = reader.IsDBNull(3) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(3));
            reader.Close();
            if (used is not null)
            {
                return new CodeConsumption(CodeStatus.Used, hostName, fingerprint);
            }

            if (now >= expires)
            {
                return new CodeConsumption(CodeStatus.Expired, hostName, fingerprint);
            }

            if (!string.Equals(fingerprint, presentedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return new CodeConsumption(CodeStatus.FingerprintMismatch, hostName, fingerprint);
            }

            Execute(connection, "UPDATE pairing_codes SET used_at = $used WHERE code_hash = $hash", ("$used", now.ToString("O")), ("$hash", codeHash));
            return new CodeConsumption(CodeStatus.Accepted, hostName, fingerprint);
        });
    }

    public DeviceRecord InsertDevice(string displayName, string credentialName, string fingerprint, string expectedHostName, string destinationId, DateTimeOffset now)
    {
        var deviceId = "d" + Guid.NewGuid().ToString("N");
        InImmediate(connection =>
        {
            Execute(
                connection,
                """
                INSERT INTO devices (device_id, display_name, credential_name, fingerprint, expected_host_name, created_at, revoked_at)
                VALUES ($id, $name, $credential, $fingerprint, $host, $created, NULL)
                """,
                ("$id", deviceId),
                ("$name", displayName),
                ("$credential", credentialName),
                ("$fingerprint", fingerprint),
                ("$host", expectedHostName),
                ("$created", now.ToString("O")));
            Execute(
                connection,
                "INSERT INTO destination_grants (device_id, destination_id) VALUES ($device, $destination)",
                ("$device", deviceId),
                ("$destination", destinationId));
        });
        return new DeviceRecord(deviceId, displayName, credentialName, fingerprint, expectedHostName, false);
    }

    public DeviceRecord? FindDevice(string deviceId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, """
                SELECT device_id, display_name, credential_name, fingerprint, expected_host_name, revoked_at
                FROM devices WHERE device_id = $id
                """);
            command.Parameters.AddWithValue("$id", deviceId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new DeviceRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                !reader.IsDBNull(5));
        }
    }

    public void RotateCredential(string deviceId, string credentialName, string fingerprint, string expectedHostName)
    {
        InImmediate(connection => Execute(
            connection,
            """
            UPDATE devices
            SET credential_name = $credential, fingerprint = $fingerprint, expected_host_name = $host
            WHERE device_id = $id AND revoked_at IS NULL
            """,
            ("$credential", credentialName),
            ("$fingerprint", fingerprint),
            ("$host", expectedHostName),
            ("$id", deviceId)));
    }

    public void Revoke(string deviceId, DateTimeOffset now)
    {
        InImmediate(connection => Execute(
            connection,
            "UPDATE devices SET revoked_at = $at WHERE device_id = $id AND revoked_at IS NULL",
            ("$at", now.ToString("O")),
            ("$id", deviceId)));
    }

    public IdempotencyRecord? FindIdempotency(string deviceId, string sourceId)
    {
        lock (_gate)
        {
            using var connection = Open();
            var row = ReadIdempotency(connection, deviceId, sourceId);
            if (row is null)
            {
                return null;
            }

            return new IdempotencyRecord(deviceId, sourceId, row.Value.Hash, row.Value.DestinationId, row.Value.JobId, ReadPhase(connection, row.Value.JobId));
        }
    }

    public void Audit(string kind, string? deviceId, string? destinationId, string? sourceId, string? jobId, long? sizeBytes, string? phase, string result)
    {
        lock (_gate)
        {
            using var connection = Open();
            Execute(
                connection,
                """
                INSERT INTO audit_events (at, kind, device_id, destination_id, source_id, job_id, size_bytes, phase, result)
                VALUES ($at, $kind, $device, $destination, $source, $job, $size, $phase, $result)
                """,
                ("$at", DateTimeOffset.UtcNow.ToString("O")),
                ("$kind", kind),
                ("$device", (object?)deviceId ?? DBNull.Value),
                ("$destination", (object?)destinationId ?? DBNull.Value),
                ("$source", (object?)sourceId ?? DBNull.Value),
                ("$job", (object?)jobId ?? DBNull.Value),
                ("$size", (object?)sizeBytes ?? DBNull.Value),
                ("$phase", (object?)phase ?? DBNull.Value),
                ("$result", result));
        }
    }

    public string ReadAuditText()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, "SELECT kind, device_id, destination_id, source_id, job_id, size_bytes, phase, result FROM audit_events");
            using var reader = command.ExecuteReader();
            var lines = new List<string>();
            while (reader.Read())
            {
                lines.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "" : reader.GetValue(index).ToString())));
            }

            return string.Join("\n", lines);
        }
    }

    public bool TryBegin(PrintJob job, string queueName)
    {
        ArgumentNullException.ThrowIfNull(job);
        JobIdRules.EnsureSafe(job.Id);
        lock (_gate)
        {
            if (!_armed.Remove(job.Id))
            {
                return false;
            }

            var record = FindLocked(job.Id);
            return record is { Phase: JobPhase.Received } && string.Equals(record.QueueName, queueName, StringComparison.Ordinal);
        }
    }

    public void Update(string id, JobPhase phase, string? detail)
    {
        lock (_gate)
        {
            if (FindLocked(id) is null)
            {
                throw new InvalidOperationException("El trabajo no está registrado.");
            }

            using var connection = Open();
            Execute(connection, "UPDATE jobs SET phase = $phase, detail = $detail WHERE job_id = $id", ("$phase", phase.ToString()), ("$detail", (object?)detail ?? DBNull.Value), ("$id", id));
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
            var record = FindLocked(id) ?? throw new InvalidOperationException("El trabajo no está registrado.");
            if (record.Phase != JobPhase.SubmittedToSpooler)
            {
                throw new InvalidOperationException("La confirmación física solo se anota cuando el spooler ya aceptó el trabajo.");
            }

            using var connection = Open();
            Execute(connection, "UPDATE jobs SET physical_confirmation = $note WHERE job_id = $id", ("$note", note.Trim()), ("$id", id));
        }
    }

    public JobRecord? Find(string id)
    {
        lock (_gate)
        {
            return FindLocked(id);
        }
    }

    public IReadOnlyList<JobRecord> List()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, "SELECT job_id FROM jobs");
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            reader.Close();
            return ids.Select(id => FindLocked(id)).OfType<JobRecord>().OrderBy(record => record.ReceivedAt).ToList();
        }
    }

    public IReadOnlyList<string> ListIds()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, "SELECT job_id FROM jobs ORDER BY received_at");
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }
    }

    public void Save(PrintDestination destination)
    {
        SyncPublished(new PublishedDestination(destination.Id, destination.DisplayName, destination.QueueName, destination.PaperName ?? ""));
    }

    public bool Remove(string id)
    {
        var removed = false;
        InImmediate(connection =>
        {
            Execute(connection, "DELETE FROM destination_grants WHERE destination_id = $id", ("$id", id));
            using var command = Command(connection, "DELETE FROM published_destinations WHERE destination_id = $id");
            command.Parameters.AddWithValue("$id", id);
            removed = command.ExecuteNonQuery() > 0;
        });
        return removed;
    }

    PrintDestination? IDestinationStore.Find(string id)
    {
        var published = FindPublished(id);
        return published is null ? null : ToPrintDestination(published);
    }

    IReadOnlyList<PrintDestination> IDestinationStore.List()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(connection, "SELECT destination_id, display_name, queue_name, paper_name FROM published_destinations");
            using var reader = command.ExecuteReader();
            var list = new List<PrintDestination>();
            while (reader.Read())
            {
                list.Add(ToPrintDestination(new PublishedDestination(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))));
            }

            return list;
        }
    }

    public void Dispose()
    {
    }

    private JobRecord? FindLocked(string id)
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT job_id, media_type, local_path, destination_id, queue_name, paper_name, options_json,
                   phase, detail, physical_confirmation, received_at
            FROM jobs WHERE job_id = $id
            """);
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        PrintOptions? options = reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<PrintOptions>(reader.GetString(6));
        return new JobRecord
        {
            Id = reader.GetString(0),
            MediaType = reader.GetString(1),
            LocalPath = reader.GetString(2),
            DestinationId = reader.GetString(3),
            QueueName = reader.GetString(4),
            PaperName = reader.IsDBNull(5) ? null : reader.GetString(5),
            Options = options,
            Phase = Enum.Parse<JobPhase>(reader.GetString(7)),
            Detail = reader.IsDBNull(8) ? null : reader.GetString(8),
            PhysicalConfirmation = reader.IsDBNull(9) ? null : reader.GetString(9),
            ReceivedAt = DateTimeOffset.Parse(reader.GetString(10)),
        };
    }

    private HashSet<string> ReferencedPaths()
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT local_path FROM jobs");
        using var reader = command.ExecuteReader();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            paths.Add(Path.GetFullPath(reader.GetString(0)));
        }

        return paths;
    }

    private string SafeDocumentPath(string jobId)
    {
        JobIdRules.EnsureSafe(jobId);
        var path = Path.GetFullPath(Path.Combine(_documents, jobId + ".pdf"));
        var root = _documents.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El archivo quedaría fuera del almacén del agente.");
        }

        return path;
    }

    private static int DeleteFiles(string directory)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
            count++;
        }

        return count;
    }

    private T InImmediate<T>(Func<SqliteConnection, T> work)
    {
        lock (_gate)
        {
            using var connection = Open();
            Execute(connection, "BEGIN IMMEDIATE");
            try
            {
                var result = work(connection);
                Execute(connection, "COMMIT");
                return result;
            }
            catch
            {
                Execute(connection, "ROLLBACK");
                throw;
            }
        }
    }

    private void InImmediate(Action<SqliteConnection> work) => InImmediate(connection =>
    {
        work(connection);
        return 0;
    });

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                device_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                credential_name TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                expected_host_name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                revoked_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS published_destinations (
                destination_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                queue_name TEXT NOT NULL,
                paper_name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS destination_grants (
                device_id TEXT NOT NULL,
                destination_id TEXT NOT NULL,
                PRIMARY KEY (device_id, destination_id),
                FOREIGN KEY (device_id) REFERENCES devices(device_id),
                FOREIGN KEY (destination_id) REFERENCES published_destinations(destination_id)
            );
            CREATE TABLE IF NOT EXISTS idempotency (
                device_id TEXT NOT NULL,
                source_id TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                destination_id TEXT NOT NULL,
                job_id TEXT NOT NULL UNIQUE,
                PRIMARY KEY (device_id, source_id)
            );
            CREATE TABLE IF NOT EXISTS jobs (
                job_id TEXT PRIMARY KEY,
                media_type TEXT NOT NULL,
                local_path TEXT NOT NULL,
                destination_id TEXT NOT NULL,
                queue_name TEXT NOT NULL,
                paper_name TEXT NULL,
                options_json TEXT NULL,
                phase TEXT NOT NULL,
                detail TEXT NULL,
                physical_confirmation TEXT NULL,
                received_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pairing_codes (
                code_hash TEXT PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                expected_host_name TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                used_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS acceptance_times (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                accepted_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS audit_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at TEXT NOT NULL,
                kind TEXT NOT NULL,
                device_id TEXT NULL,
                destination_id TEXT NULL,
                source_id TEXT NULL,
                job_id TEXT NULL,
                size_bytes INTEGER NULL,
                phase TEXT NULL,
                result TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static PublishedDestination? ReadPublished(SqliteConnection connection, string destinationId)
    {
        using var command = Command(connection, """
            SELECT destination_id, display_name, queue_name, paper_name
            FROM published_destinations WHERE destination_id = $id
            """);
        command.Parameters.AddWithValue("$id", destinationId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new PublishedDestination(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    private static bool GrantExists(SqliteConnection connection, string deviceId, string destinationId)
    {
        using var command = Command(connection, """
            SELECT 1 FROM destination_grants WHERE device_id = $device AND destination_id = $destination
            """);
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$destination", destinationId);
        return command.ExecuteScalar() is not null;
    }

    private static (string Hash, string DestinationId, string JobId)? ReadIdempotency(SqliteConnection connection, string deviceId, string sourceId)
    {
        using var command = Command(connection, """
            SELECT content_sha256, destination_id, job_id
            FROM idempotency WHERE device_id = $device AND source_id = $source
            """);
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$source", sourceId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    private static JobPhase ReadPhase(SqliteConnection connection, string jobId)
    {
        using var command = Command(connection, "SELECT phase FROM jobs WHERE job_id = $id");
        command.Parameters.AddWithValue("$id", jobId);
        var value = (string?)command.ExecuteScalar();
        return value is null ? JobPhase.Failed : Enum.Parse<JobPhase>(value);
    }

    private static int CountRecentAccepts(SqliteConnection connection, string deviceId, DateTimeOffset now)
    {
        using var command = Command(connection, """
            SELECT accepted_at FROM acceptance_times WHERE device_id = $device
            """);
        command.Parameters.AddWithValue("$device", deviceId);
        using var reader = command.ExecuteReader();
        var count = 0;
        var start = now - AcceptWindow;
        while (reader.Read())
        {
            if (DateTimeOffset.Parse(reader.GetString(0)) >= start)
            {
                count++;
            }
        }

        return count;
    }

    private static PrintDestination ToPrintDestination(PublishedDestination destination) =>
        new(destination.Id, destination.DisplayName, destination.QueueName, destination.PaperName, PrintOptions.ForPaper(destination.PaperName));

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}

public enum CodeStatus
{
    Accepted,
    Unknown,
    Used,
    Expired,
    FingerprintMismatch,
}

public sealed record CodeConsumption(CodeStatus Status, string? ExpectedHostName, string? Fingerprint);

public sealed record DeviceRecord(string Id, string DisplayName, string CredentialName, string Fingerprint, string ExpectedHostName, bool Revoked);

public sealed record IdempotencyRecord(string DeviceId, string SourceId, string ContentSha256, string DestinationId, string JobId, JobPhase Phase);
