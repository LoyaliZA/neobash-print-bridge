using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neobash.PrintBridge.Core.Dispatch;

internal static class DispatchJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
