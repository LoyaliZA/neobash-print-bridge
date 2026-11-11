using System.Net;
using System.Text.Json;

namespace Neobash.PrintBridge.Lan;

public sealed class AgentConfig
{
    public int Port { get; set; } = 8741;

    public string ListenAddress { get; set; } = "";

    public string HostName { get; set; } = "";

    public string DataDirectory { get; set; } = "";

    public string ResolveDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DataDirectory))
        {
            return Path.GetFullPath(DataDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Neobash",
            "PrintBridge",
            "Agent");
    }

    public string ResolveHostName() =>
        string.IsNullOrWhiteSpace(HostName) ? Environment.MachineName : HostName.Trim();

    public IPAddress ResolveListenAddress() =>
        string.IsNullOrWhiteSpace(ListenAddress) ? IPAddress.Any : IPAddress.Parse(ListenAddress.Trim());
}

public sealed record PublishedDestination(string Id, string DisplayName, string QueueName, string PaperName);

public static class DestinationFile
{
    public const string FileName = "destination.local.json";

    public static PublishedDestination? Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(path), JsonOptions());
        if (model is null
            || string.IsNullOrWhiteSpace(model.Id)
            || string.IsNullOrWhiteSpace(model.DisplayName)
            || string.IsNullOrWhiteSpace(model.QueueName)
            || string.IsNullOrWhiteSpace(model.PaperName))
        {
            throw new InvalidOperationException("destination.local.json no trae identificador, nombre visible, cola y papel.");
        }

        return new PublishedDestination(model.Id.Trim(), model.DisplayName.Trim(), model.QueueName.Trim(), model.PaperName.Trim());
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Model
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? QueueName { get; set; }

        public string? PaperName { get; set; }
    }
}
