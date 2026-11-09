using System.Diagnostics;
using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class CachingPrinterCatalog : IPrinterCatalog
{
    private readonly IPrinterProbe _probe;
    private readonly IDiagnosticLog? _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DetailFlight> _flights = new(StringComparer.OrdinalIgnoreCase);
    private List<InstalledQueue> _queues = [];
    private List<string> _warnings = [];
    private int _generation;
    private Task? _refreshFlight;
    private bool _rerunRefresh;

    public CachingPrinterCatalog(IPrinterProbe probe, IDiagnosticLog? log = null)
    {
        _probe = probe;
        _log = log;
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate)
            {
                return _warnings.ToArray();
            }
        }
    }

    public string? RefreshError { get; private set; }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            DowngradeLocked();
            if (_refreshFlight is not null)
            {
                _rerunRefresh = true;
            }
        }
    }

    public Task RefreshNamesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_refreshFlight is not null)
            {
                return _refreshFlight;
            }

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshFlight = started.Task;
            _ = Task.Run(() => RefreshLoop(started, cancellationToken));
            return started.Task;
        }
    }

    public Task<InstalledQueue?> ReadDetailsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        lock (_gate)
        {
            var generation = _generation;
            if (TryCached(queueName, out var cached))
            {
                return Task.FromResult(cached);
            }

            if (_flights.TryGetValue(queueName, out var flight) && flight.Generation == generation)
            {
                return flight.Task;
            }

            var current = new DetailFlight(generation, new(TaskCreationOptions.RunContinuationsAsynchronously));
            _flights[queueName] = current;
            _ = CompleteDetailsAsync(queueName, current, cancellationToken);
            return current.Task;
        }
    }

    public InstalledQueue? Find(string queueName) =>
        ReadDetailsAsync(queueName).GetAwaiter().GetResult();

    public IReadOnlyList<InstalledQueue> List()
    {
        lock (_gate)
        {
            return _queues.ToArray();
        }
    }

    private async Task RefreshLoop(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int generation;
                lock (_gate)
                {
                    _rerunRefresh = false;
                    generation = ++_generation;
                    DowngradeLocked();
                    RefreshError = null;
                }

                IReadOnlyList<string>? names = null;
                Exception? error = null;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    names = await Task.Run(() => _probe.ListNames(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    error = exception;
                }

                var elapsed = Stopwatch.GetElapsedTime(started);

                var publishTiming = false;
                lock (_gate)
                {
                    if (generation == _generation)
                    {
                        if (error is null && names is not null)
                        {
                            PublishNamesLocked(names);
                        }
                        else if (error is not null)
                        {
                            RefreshError = error.Message;
                            _warnings = [$"No se pudo enumerar las colas: {error.Message}"];
                        }

                        publishTiming = true;
                    }
                }

                if (publishTiming)
                {
                    _log?.Timing(
                        "queue-name-enumeration",
                        elapsed,
                        ("baseline", "probe"),
                        ("result", error is null ? "ready" : "unreadable"),
                        ("count", (names?.Count ?? 0).ToString()));
                }

                lock (_gate)
                {
                    // Invalidation and retirement must be one atomic decision.
                    if (!_rerunRefresh)
                    {
                        _refreshFlight = null;
                        completion.TrySetResult();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            RetireRefresh(completion);
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            RetireRefresh(completion);
            completion.TrySetException(exception);
        }
    }

    private void RetireRefresh(TaskCompletionSource completion)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_refreshFlight, completion.Task))
            {
                _refreshFlight = null;
            }
        }
    }

    private async Task CompleteDetailsAsync(string queueName, DetailFlight flight, CancellationToken cancellationToken)
    {
        InstalledQueue? result = null;
        Exception? error = null;
        try
        {
            result = await ReadDetailsCore(queueName, flight.Generation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            lock (_gate)
            {
                if (_flights.TryGetValue(queueName, out var current) && ReferenceEquals(current, flight))
                {
                    _flights.Remove(queueName);
                }
            }
        }

        if (error is OperationCanceledException)
        {
            flight.Completion.TrySetCanceled(cancellationToken);
        }
        else if (error is not null)
        {
            flight.Completion.TrySetException(error);
        }
        else
        {
            flight.Completion.TrySetResult(result);
        }
    }

    private async Task<InstalledQueue?> ReadDetailsCore(string queueName, int generation, CancellationToken cancellationToken)
    {
        InstalledQueue? read = null;
        Exception? error = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            read = await Task.Run(() => _probe.ReadDetails(queueName), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        InstalledQueue? published = null;
        string? outcome = null;
        lock (_gate)
        {
            if (generation != _generation)
            {
                return null;
            }

            if (error is not null)
            {
                MarkUnreadableLocked(queueName, error.Message);
                outcome = "unreadable";
            }
            else if (read is null)
            {
                MarkUnreadableLocked(queueName, "La cola ya no está disponible.");
                outcome = "unreadable";
            }
            else
            {
                published = read with { QueueName = queueName, Detail = QueueDetail.Ready };
                ReplaceLocked(published);
                _warnings.RemoveAll(warning => warning.Contains($"«{queueName}»", StringComparison.OrdinalIgnoreCase));
                outcome = "ready";
            }
        }

        if (outcome is not null)
        {
            LogCapabilities(queueName, elapsed, outcome);
        }

        return published;
    }

    private void LogCapabilities(string queueName, TimeSpan elapsed, string outcome) =>
        _log?.Timing(
            "queue-capabilities",
            elapsed,
            ("baseline", "probe"),
            ("queue", queueName),
            ("result", outcome));

    private bool TryCached(string queueName, out InstalledQueue? cached)
    {
        var existing = FindLocked(queueName);
        if (existing is { Detail: QueueDetail.Ready })
        {
            cached = existing;
            return true;
        }

        if (existing is { Detail: QueueDetail.Unreadable })
        {
            cached = null;
            return true;
        }

        cached = null;
        return false;
    }

    private void PublishNamesLocked(IReadOnlyList<string> names)
    {
        var previous = _queues.ToDictionary(queue => queue.QueueName, StringComparer.OrdinalIgnoreCase);
        var published = new List<InstalledQueue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            if (previous.TryGetValue(name, out var existing) && existing.Detail == QueueDetail.Ready)
            {
                published.Add(existing);
                continue;
            }

            published.Add(new InstalledQueue(name, "", [], Detail: QueueDetail.NameOnly));
        }

        _queues = published;
        _warnings = [];
        RefreshError = null;
    }

    private void DowngradeLocked()
    {
        _queues = _queues.Select(queue => queue with
        {
            DriverName = "",
            PaperNames = [],
            MaximumCopies = 1,
            SupportsLandscape = false,
            SupportsPortrait = true,
            Detail = QueueDetail.NameOnly,
        }).ToList();
        _warnings = [];
    }

    private void MarkUnreadableLocked(string queueName, string message)
    {
        var warning = $"No se pudo leer la cola «{queueName}»: {message}";
        if (!_warnings.Contains(warning, StringComparer.Ordinal))
        {
            _warnings.Add(warning);
        }

        var unreadable = new InstalledQueue(queueName, "", [], Detail: QueueDetail.Unreadable);
        var index = _queues.FindIndex(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            unreadable = _queues[index] with
            {
                DriverName = "",
                PaperNames = [],
                Detail = QueueDetail.Unreadable,
            };
            _queues[index] = unreadable;
            return;
        }

        _queues.Add(unreadable);
    }

    private void ReplaceLocked(InstalledQueue queue)
    {
        var index = _queues.FindIndex(existing => string.Equals(existing.QueueName, queue.QueueName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _queues[index] = queue;
            return;
        }

        _queues.Add(queue);
    }

    private InstalledQueue? FindLocked(string queueName) =>
        _queues.FirstOrDefault(queue => string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase));

    private sealed record DetailFlight(int Generation, TaskCompletionSource<InstalledQueue?> Completion)
    {
        public Task<InstalledQueue?> Task => Completion.Task;
    }
}
