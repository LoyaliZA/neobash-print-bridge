using System.Text.Json;

namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class FileDestinationStore : IDestinationStore
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileDestinationStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("La ruta de almacenamiento no es válida.", nameof(root));
        }

        Directory.CreateDirectory(root);
        _path = Path.Combine(Path.GetFullPath(root), "destinations.json");
    }

    public void Save(PrintDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(destination.Id))
        {
            throw new ArgumentException("El destino necesita un identificador.", nameof(destination));
        }

        lock (_gate)
        {
            var items = Read();
            var index = items.FindIndex(item => string.Equals(item.Id, destination.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                items[index] = destination;
            }
            else
            {
                items.Add(destination);
            }

            Write(items);
        }
    }

    public PrintDestination? Find(string id)
    {
        lock (_gate)
        {
            return Read().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<PrintDestination> List()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    private List<PrintDestination> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<PrintDestination>>(json, DispatchJson.Options) ?? [];
    }

    private void Write(List<PrintDestination> items)
    {
        var json = JsonSerializer.Serialize(items, DispatchJson.Options);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }
}
