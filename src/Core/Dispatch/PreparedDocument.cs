namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class PreparedDocument
{
    public PreparedDocument(string mediaType, object content, string? name = null)
    {
        MediaType = mediaType;
        Content = content;
        Name = string.IsNullOrWhiteSpace(name) ? mediaType : name;
    }

    public string MediaType { get; }

    public object Content { get; }

    public string Name { get; }
}
