namespace Neobash.PrintBridge.Core.Capture;

public sealed class FileSystemJobFileStore : IJobFileStore
{
    private readonly string _root;

    public FileSystemJobFileStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("La ruta de almacenamiento no es válida.", nameof(root));
        }

        _root = Path.GetFullPath(root);
    }

    public IJobWorkspace Open(string jobId)
    {
        JobIdRules.EnsureSafe(jobId);
        Directory.CreateDirectory(_root);

        var path = Path.GetFullPath(Path.Combine(_root, jobId));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El trabajo quedaría fuera del almacén configurado.");
        }

        if (Directory.Exists(path))
        {
            throw new IOException("El directorio del trabajo ya existe.");
        }

        Directory.CreateDirectory(path);
        return new FileSystemJobWorkspace(path);
    }

    private sealed class FileSystemJobWorkspace : IJobWorkspace
    {
        public FileSystemJobWorkspace(string location)
        {
            Location = location;
        }

        public string Location { get; }

        public Stream CreateExclusive(string fileName)
        {
            var path = SafePath(fileName);
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        }

        public void WriteText(string fileName, string content)
        {
            var path = SafePath(fileName);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }

        private string SafePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            {
                throw new ArgumentException("El nombre de archivo no es seguro.", nameof(fileName));
            }

            return Path.Combine(Location, fileName);
        }
    }
}
