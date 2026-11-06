using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class JobCaptureStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "npb-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedJobIdGenerator _ids = new();
    private readonly JobCaptureStore _store;

    public JobCaptureStoreTests()
    {
        _store = new JobCaptureStore(new FileSystemJobFileStore(_root), _ids);
    }

    [Fact]
    public async Task Captures_opaque_bytes_into_a_unique_directory()
    {
        var result = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([7, 8, 9]));

        Assert.Equal(JobCaptureStatus.Succeeded, result.Status);
        Assert.False(result.OpenXpsStructureValidated);
        Assert.True(File.Exists(result.DocumentPath));
        Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(result.DocumentPath!));
        Assert.Equal(3, result.SizeBytes);
        Assert.EndsWith("document.oxps", result.DocumentPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Does_not_report_success_for_empty_content()
    {
        var result = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream());

        Assert.Equal(JobCaptureStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        var metadata = await File.ReadAllTextAsync(Path.Combine(result.DirectoryPath!, "metadata.json"));
        Assert.Contains("\"estado\": \"Failed\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"tamanoBytes\": 0", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_overwrite_an_existing_job()
    {
        _ids.Enqueue("job-estable-001");
        _ids.Enqueue("job-estable-001");
        var first = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([1, 2]));
        var second = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([3, 4]));

        Assert.Equal(JobCaptureStatus.Succeeded, first.Status);
        Assert.Equal(JobCaptureStatus.Failed, second.Status);
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(first.DocumentPath!));
    }

    [Fact]
    public async Task Three_captures_create_three_independent_directories()
    {
        var results = new List<JobCaptureResult>();
        for (var index = 0; index < 3; index++)
        {
            results.Add(await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([(byte)index])));
        }

        Assert.Equal(3, results.Select(result => result.JobId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, results.Select(result => result.DirectoryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(results, result => Assert.Equal(JobCaptureStatus.Succeeded, result.Status));
    }

    [Fact]
    public async Task Rejects_an_unsafe_job_identifier_without_writing()
    {
        _ids.Enqueue("..\\fuera");

        var result = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([1]));

        Assert.Equal(JobCaptureStatus.Failed, result.Status);
        Assert.False(Directory.Exists(Path.Combine(_root, "..\\fuera")));
    }

    [Fact]
    public async Task Writes_metadata_only_after_the_document_is_closed()
    {
        var result = await _store.CaptureAsync(
            Request("application/oxps", "NPB - Prueba Virtual", "neobash-print-bridge:npb-prueba-virtual", "Bloc de notas"),
            new MemoryStream([4, 5]));

        var metadata = await File.ReadAllTextAsync(Path.Combine(result.DirectoryPath!, "metadata.json"));
        Assert.Contains("\"impresoraVirtual\": \"NPB - Prueba Virtual\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"formato\": \"application/oxps\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"tamanoBytes\": 2", metadata, StringComparison.Ordinal);
        Assert.Contains("\"aplicacionOrigen\": \"Bloc de notas\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"estado\": \"Succeeded\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("openXps", metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Uses_the_injected_root_and_keeps_another_root_unchanged()
    {
        var otherRoot = Path.Combine(Path.GetTempPath(), "npb-tests", Guid.NewGuid().ToString("N"));
        var other = new JobCaptureStore(new FileSystemJobFileStore(otherRoot), new FixedJobIdGenerator());

        await _store.CaptureAsync(Request("application/octet-stream"), new MemoryStream([1]));
        await other.CaptureAsync(Request("application/octet-stream"), new MemoryStream([2]));

        Assert.Single(Directory.GetDirectories(_root));
        Assert.Single(Directory.GetDirectories(otherRoot));
        Directory.Delete(otherRoot, recursive: true);
    }

    [Fact]
    public async Task Reports_cancellation_without_success()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var result = await _store.CaptureAsync(Request("application/oxps"), new MemoryStream([1, 2, 3]), canceled.Token);

        Assert.Equal(JobCaptureStatus.Canceled, result.Status);
        Assert.NotEqual(JobCaptureStatus.Succeeded, result.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JobCaptureRequest Request(
        string contentType,
        string printer = "NPB - Prueba Virtual",
        string? uri = "neobash-print-bridge:npb-prueba-virtual",
        string? source = null) =>
        new()
        {
            PrinterName = printer,
            PrinterUri = uri,
            SourceApplication = source,
            ContentType = contentType,
            ReceivedAt = new DateTimeOffset(2026, 11, 6, 22, 0, 0, TimeSpan.Zero)
        };

    private sealed class FixedJobIdGenerator : IJobIdGenerator
    {
        private readonly Queue<string> _ids = new();
        private int _sequence;

        public void Enqueue(string jobId) => _ids.Enqueue(jobId);

        public string Create() => _ids.Count > 0 ? _ids.Dequeue() : $"job-{Interlocked.Increment(ref _sequence):000}";
    }
}
