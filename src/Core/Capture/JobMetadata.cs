using System.Text.Json.Serialization;

namespace Neobash.PrintBridge.Core.Capture;

public sealed class JobMetadata
{
    [JsonPropertyName("identificador")]
    public required string JobId { get; init; }

    [JsonPropertyName("fecha")]
    public required DateTimeOffset ReceivedAt { get; init; }

    [JsonPropertyName("impresoraVirtual")]
    public required string PrinterName { get; init; }

    [JsonPropertyName("uriImpresora")]
    public string? PrinterUri { get; init; }

    [JsonPropertyName("aplicacionOrigen")]
    public string? SourceApplication { get; init; }

    [JsonPropertyName("formato")]
    public required string ContentType { get; init; }

    [JsonPropertyName("tamanoBytes")]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("estado")]
    public required string Status { get; init; }

    [JsonPropertyName("archivo")]
    public required string DocumentFileName { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
