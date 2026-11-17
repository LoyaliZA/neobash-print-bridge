using System.Text.Json.Serialization;

namespace Neobash.PrintBridge.Core.Dispatch;

public sealed record PrintDestination(
    string Id,
    string DisplayName,
    string QueueName,
    string? PaperName,
    PrintOptions? Defaults = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DriverName = null);
