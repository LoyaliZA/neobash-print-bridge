using Neobash.PrintBridge.Core.Capture;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Tests;

public sealed class DesktopStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "npb-desktop-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Destination_keeps_its_identifier_when_reloaded()
    {
        var store = new FileDestinationStore(_root);
        store.Save(new PrintDestination("dest-estable", "Star de mostrador", "Star BSC10", "Rollo 80 x 297 mm"));

        var loaded = new FileDestinationStore(_root).Find("dest-estable");

        Assert.NotNull(loaded);
        Assert.Equal("dest-estable", loaded.Id);
        Assert.Equal("Star BSC10", loaded.QueueName);
        Assert.Equal("Rollo 80 x 297 mm", loaded.PaperName);
        Assert.Equal("Star de mostrador", loaded.DisplayName);
        Assert.Null(loaded.Defaults);
    }

    [Fact]
    public void Destination_defaults_round_trip_without_becoming_universal()
    {
        var store = new FileDestinationStore(_root);
        var star = new PrintOptions
        {
            PaperName = "80mm x Receipt (48 Columns)",
            Copies = 1,
            ScaleMode = ScaleMode.Percentage,
            ScalePercent = 90,
            PagesPerSheet = 1,
        };
        store.Save(new PrintDestination("dest-star", "Star", "Star BSC10 (Copiar 1)", star.PaperName, star));
        store.Save(new PrintDestination("dest-hoja", "Hojas", "Otra impresora", "A4", PrintOptions.ForPaper("A4")));

        var reloaded = new FileDestinationStore(_root);
        Assert.Equal(90, reloaded.Find("dest-star")!.Defaults!.ScalePercent);
        Assert.Equal(ScaleMode.ActualSize, reloaded.Find("dest-hoja")!.Defaults!.ScaleMode);
        Assert.Null(reloaded.Find("dest-hoja")!.Defaults!.ScalePercent);
    }

    [Fact]
    public void Job_history_keeps_the_options_that_were_used()
    {
        var options = new PrintOptions
        {
            PaperName = "80mm x Receipt (48 Columns)",
            Copies = 1,
            Range = new PageRange(1, 1),
            Orientation = PageOrientation.Portrait,
            ScaleMode = ScaleMode.Percentage,
            ScalePercent = 90,
            PagesPerSheet = 1,
        };
        var store = new FileJobRecordStore(_root);
        var job = new PrintJob("trabajo-opciones", "application/pdf", @"C:\documentos\muestra.pdf", "dest-star", options.PaperName, options);
        Assert.True(store.TryBegin(job, "Star BSC10 (Copiar 1)"));

        var record = new FileJobRecordStore(_root).Find("trabajo-opciones");
        Assert.NotNull(record);
        Assert.Equal(90, record.Options!.ScalePercent);
        Assert.Equal(1, record.Options.PagesPerSheet);
        Assert.Equal(1, record.Options.Range!.FirstPage);
    }

    [Fact]
    public void Job_intent_survives_a_new_store_and_blocks_a_second_begin()
    {
        var store = new FileJobRecordStore(_root);
        var job = new PrintJob("trabajo-persistido", "application/pdf", @"C:\documentos\muestra.pdf", "dest-estable", "A4");
        Assert.True(store.TryBegin(job, "Star BSC10"));

        var reloaded = new FileJobRecordStore(_root);
        var record = reloaded.Find("trabajo-persistido");

        Assert.NotNull(record);
        Assert.Equal(JobPhase.Received, record.Phase);
        Assert.Equal("Star BSC10", record.QueueName);
        Assert.False(reloaded.TryBegin(job, "Otra impresora"));
        Assert.Equal("Star BSC10", reloaded.Find("trabajo-persistido")!.QueueName);
    }

    [Fact]
    public void Physical_confirmation_is_stored_without_changing_the_spooler_phase()
    {
        var store = new FileJobRecordStore(_root);
        var job = new PrintJob("trabajo-visto", "application/pdf", @"C:\documentos\muestra.pdf", "dest-estable", "A4");
        Assert.True(store.TryBegin(job, "Star BSC10"));
        store.Update("trabajo-visto", JobPhase.SubmittedToSpooler, "El spooler aceptó el trabajo.");

        store.RecordPhysicalConfirmation("trabajo-visto", "Quien opera vio el papel.");

        var record = new FileJobRecordStore(_root).Find("trabajo-visto");
        Assert.NotNull(record);
        Assert.Equal(JobPhase.SubmittedToSpooler, record.Phase);
        Assert.Equal("Quien opera vio el papel.", record.PhysicalConfirmation);
    }

    [Fact]
    public void Recovery_snapshot_leaves_a_later_job_received()
    {
        var store = new FileJobRecordStore(_root);
        store.TryBegin(new PrintJob("trabajo-previo", "application/pdf", @"C:\documentos\muestra.pdf", "dest-estable", "A4"), "Star BSC10");
        store.Update("trabajo-previo", JobPhase.Prepared, "Páginas listas.");
        var ids = store.ListIds();
        store.TryBegin(new PrintJob("trabajo-nuevo", "application/pdf", @"C:\documentos\otro.pdf", "dest-estable", "A4"), "Star BSC10");

        JobRecovery.MarkInterruptedAsUncertain(store, ids);

        Assert.Equal(JobPhase.Uncertain, store.Find("trabajo-previo")!.Phase);
        Assert.Equal(JobPhase.Received, store.Find("trabajo-nuevo")!.Phase);
    }

    [Fact]
    public void Timing_line_names_the_operation_without_replacing_the_message_log()
    {
        var path = Path.Combine(_root, "diagnostic.log");
        var log = new FileDiagnosticLog(path);
        log.Timing("ui-responsive", TimeSpan.FromMilliseconds(12.4), ("baseline", "managed-entry"));

        var line = File.ReadAllText(path);
        Assert.Contains("timing operation=ui-responsive", line, StringComparison.Ordinal);
        Assert.Contains("elapsedMs=12", line, StringComparison.Ordinal);
        Assert.Contains("baseline=managed-entry", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Interrupted_received_job_becomes_uncertain_on_disk()
    {
        var store = new FileJobRecordStore(_root);
        store.TryBegin(new PrintJob("trabajo-abierto", "application/pdf", @"C:\documentos\muestra.pdf", "dest-estable", "A4"), "Star BSC10");

        JobRecovery.MarkInterruptedAsUncertain(new FileJobRecordStore(_root));

        var record = new FileJobRecordStore(_root).Find("trabajo-abierto");
        Assert.NotNull(record);
        Assert.Equal(JobPhase.Uncertain, record.Phase);
        Assert.Null(record.PhysicalConfirmation);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
