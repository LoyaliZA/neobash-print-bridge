using System.Net;
using System.Text;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Lan;

public static class ClientConsole
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, ISecretStore secrets, string dataDirectory, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            error.WriteLine("Comandos: pair, send, status, destinations, renew.");
            return 2;
        }

        var options = Options(args);
        var trust = ClientTrust.Load(dataDirectory);
        try
        {
            switch (args[0])
            {
                case "pair":
                    Require(options, "host", "port", "fingerprint", "code");
                    trust.RememberOperatorPin(options["host"], int.Parse(options["port"]), options["fingerprint"]);
                    trust.Save(dataDirectory);
                    using (var client = new LanClient(trust))
                    {
                        var paired = await client.PairAsync(options["code"], options.GetValueOrDefault("name"), trust.DeviceId, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(paired.DeviceId) || string.IsNullOrWhiteSpace(paired.Credential))
                        {
                            error.WriteLine(paired.Detail ?? "No se pudo vincular.");
                            return 1;
                        }

                        trust.DeviceId = paired.DeviceId;
                        secrets.Put(ClientTrust.CredentialSecretName, Encoding.UTF8.GetBytes(paired.Credential));
                        trust.Save(dataDirectory);
                        output.WriteLine("Vinculado.");
                        output.WriteLine("Dispositivo: " + paired.DeviceId);
                        output.WriteLine("Destino: " + paired.DestinationName + " (" + paired.DestinationId + ")");
                    }

                    return 0;
                case "send":
                    Require(options, "destination", "source", "pdf");
                    return await WithClient(trust, secrets, dataDirectory, output, error, async (client, credential) =>
                    {
                        await using var pdf = File.OpenRead(options["pdf"]);
                        var sent = await client.SendAsync(options["destination"], options["source"], pdf, credential, cancellationToken).ConfigureAwait(false);
                        output.WriteLine(sent.Transfer + " " + sent.Phase);
                        output.WriteLine(sent.Detail);
                        return sent.Transfer == nameof(TransferState.Aceptada) ? 0 : 1;
                    }).ConfigureAwait(false);
                case "status":
                    Require(options, "source");
                    return await WithClient(trust, secrets, dataDirectory, output, error, async (client, credential) =>
                    {
                        var status = await client.StatusAsync(options["source"], credential, cancellationToken).ConfigureAwait(false);
                        output.WriteLine(status.Transfer + " " + status.Phase);
                        output.WriteLine(status.Detail);
                        return 0;
                    }).ConfigureAwait(false);
                case "destinations":
                    return await WithClient(trust, secrets, dataDirectory, output, error, async (client, credential) =>
                    {
                        output.WriteLine(await client.DestinationsAsync(credential, cancellationToken).ConfigureAwait(false));
                        return 0;
                    }).ConfigureAwait(false);
                case "renew":
                    Require(options, "fingerprint");
                    return await WithClient(trust, secrets, dataDirectory, output, error, async (client, credential) =>
                    {
                        var confirmed = await client.ConfirmAsync(options["fingerprint"], credential, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(confirmed.Credential))
                        {
                            error.WriteLine(confirmed.Detail ?? "No se pudo confirmar el certificado.");
                            return 1;
                        }

                        trust.RememberOperatorPin(trust.HostName, trust.Port, options["fingerprint"]);
                        secrets.Put(ClientTrust.CredentialSecretName, Encoding.UTF8.GetBytes(confirmed.Credential));
                        trust.Save(dataDirectory);
                        output.WriteLine(confirmed.Detail);
                        return 0;
                    }).ConfigureAwait(false);
                default:
                    error.WriteLine("Comando no reconocido.");
                    return 2;
            }
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> WithClient(ClientTrust trust, ISecretStore secrets, string dataDirectory, TextWriter output, TextWriter error, Func<LanClient, string, Task<int>> action)
    {
        _ = output;
        _ = dataDirectory;
        if (string.IsNullOrWhiteSpace(trust.Fingerprint) || string.IsNullOrWhiteSpace(trust.HostName) || trust.Port == 0)
        {
            error.WriteLine("Falta la vinculación. La huella se escribe desde el equipo anfitrión, no desde la conexión.");
            return 2;
        }

        var stored = secrets.TryGet(ClientTrust.CredentialSecretName);
        if (stored is null || string.IsNullOrWhiteSpace(trust.DeviceId))
        {
            error.WriteLine("Falta la credencial del dispositivo.");
            return 2;
        }

        using var client = new LanClient(trust);
        return await action(client, Encoding.UTF8.GetString(stored)).ConfigureAwait(false);
    }

    private static void Require(Dictionary<string, string> options, params string[] names)
    {
        foreach (var name in names)
        {
            if (!options.ContainsKey(name) || string.IsNullOrWhiteSpace(options[name]))
            {
                var message = name == "fingerprint"
                    ? "Falta la huella. Hay que escribirla desde el equipo anfitrión, no desde la conexión."
                    : "Falta --" + name + ".";
                throw new ArgumentException(message);
            }
        }
    }

    public static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[index][2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[++index] : "";
            options[key] = value;
        }

        return options;
    }
}

public static class AgentConsole
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, ISecretStore secrets, IPrintEngine engine, AgentConfig config, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "listen")
        {
            using var session = LanSession.Open(config, secrets, engine);
            session.Describe(output, session.IssuePairingCode());
            await using var host = await session.ListenAsync(config.ResolveListenAddress(), config.Port, cancellationToken).ConfigureAwait(false);
            output.WriteLine("Escucha: " + host.BaseUri);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        using var local = LanSession.Open(config, secrets, engine, recover: false);
        switch (args[0])
        {
            case "pair":
                local.Describe(output, local.IssuePairingCode());
                return 0;
            case "revoke":
                if (args.Length < 2)
                {
                    error.WriteLine("Falta el identificador del dispositivo.");
                    return 2;
                }

                output.WriteLine(local.Pairing.Revoke(args[1]) ? "Dispositivo revocado. El historial de trabajos se conserva." : "No está ese dispositivo.");
                return 0;
            case "renew":
                var replaced = local.ReplaceHostCertificate();
                local.Describe(output, local.IssuePairingCode());
                output.WriteLine("Certificado reemplazado. Hay que reiniciar la escucha para presentarlo.");
                _ = replaced;
                return 0;
            case "stage":
                var pending = local.Pairing.StageRenewal(local.ExpectedHostName);
                output.WriteLine("Nombre: " + pending.HostName);
                output.WriteLine("Huella nueva: " + HostCertificateTrust.Fingerprint(pending.Certificate));
                output.WriteLine("Confirmarla en el cliente con renew. Un anuncio por la red no cambia la huella guardada.");
                return 0;
            default:
                error.WriteLine("Comandos: listen, pair, revoke, renew, stage.");
                return 2;
        }
    }
}

public sealed class FixedCatalog : IPrintEngine
{
    private readonly InstalledQueue _queue;

    public FixedCatalog(string queueName, string paperName)
    {
        _queue = new InstalledQueue(queueName, "Controlador de prueba", [paperName], 99, true);
        Catalog = new QueueCatalog(_queue);
        Preparer = new ReadyPreparer();
        Spooler = new CountingSpooler();
    }

    public IDocumentPreparer Preparer { get; }

    public ISpoolerPort Spooler { get; }

    public IPrinterCatalog Catalog { get; }

    public CountingSpooler Counting => (CountingSpooler)Spooler;

    private sealed class QueueCatalog(InstalledQueue queue) : IPrinterCatalog
    {
        public InstalledQueue? Find(string queueName) =>
            string.Equals(queue.QueueName, queueName, StringComparison.OrdinalIgnoreCase) ? queue : null;

        public IReadOnlyList<InstalledQueue> List() => [queue];
    }

    private sealed class ReadyPreparer : IDocumentPreparer
    {
        public Task<PreparedDocument> PrepareAsync(PrintJob job, CancellationToken cancellationToken)
        {
            var options = job.Options ?? PrintOptions.ForPaper(job.PaperName ?? "");
            var prepared = new PreparedDocument(
                PhysicalQueueRules.PdfMediaType,
                new PreparedPdf(job.LocalPath, options, [1]),
                job.Id);
            return Task.FromResult(prepared);
        }
    }
}

public sealed class CountingSpooler : ISpoolerPort
{
    private int _calls;

    public int Calls => _calls;

    public Func<PreparedDocument, Task>? BeforeReturn { get; set; }

    public bool SawCommittedJob { get; private set; }

    public Func<string, bool>? CommitVisible { get; set; }

    public async Task SubmitAsync(PreparedDocument document, string queueName, string? paperName, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        SawCommittedJob = CommitVisible?.Invoke(document.Name) ?? true;
        if (BeforeReturn is not null)
        {
            await BeforeReturn(document).ConfigureAwait(false);
        }

        _ = queueName;
        _ = paperName;
        _ = cancellationToken;
    }
}
