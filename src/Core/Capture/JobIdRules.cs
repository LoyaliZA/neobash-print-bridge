namespace Neobash.PrintBridge.Core.Capture;

public static class JobIdRules
{
    public static void EnsureSafe(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 80 || jobId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("El identificador del trabajo no es seguro.", nameof(jobId));
        }

        if (jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !char.IsAsciiLetterOrDigit(jobId[0]))
        {
            throw new ArgumentException("El identificador del trabajo no es seguro.", nameof(jobId));
        }
    }
}
