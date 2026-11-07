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
