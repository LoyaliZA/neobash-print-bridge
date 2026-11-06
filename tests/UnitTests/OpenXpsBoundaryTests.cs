using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class OpenXpsBoundaryTests
{
    [Fact]
    public void Structural_OpenXPS_validation_is_not_part_of_this_capture_component()
    {
        Assert.False(OpenXpsLimits.StructuralValidationAvailable);
    }

    [Fact]
    public async Task Zip_signature_is_stored_as_opaque_bytes_without_an_OpenXPS_validity_claim()
    {
        var root = Path.Combine(Path.GetTempPath(), "npb-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JobCaptureStore(new FileSystemJobFileStore(root), new SequenceJobIdGenerator());
            var payload = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
            var result = await store.CaptureAsync(new JobCaptureRequest
            {
                PrinterName = "NPB - Prueba Virtual",
                ContentType = "application/oxps",
                ReceivedAt = DateTimeOffset.Parse("2026-11-06T22:00:00Z")
            }, new MemoryStream(payload));

            Assert.Equal(JobCaptureStatus.Succeeded, result.Status);
            Assert.False(result.OpenXpsStructureValidated);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.DocumentPath!));

            var metadata = await File.ReadAllTextAsync(Path.Combine(result.DirectoryPath!, "metadata.json"));
            Assert.DoesNotContain("openXpsValido", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("estructuraOxps", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"formato\": \"application/oxps\"", metadata, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class SequenceJobIdGenerator : IJobIdGenerator
    {
        public string Create() => "oxps-boundary-001";
    }
}
