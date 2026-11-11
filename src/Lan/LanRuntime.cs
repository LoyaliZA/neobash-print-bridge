using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Lan;

public interface IPrintEngine
{
    IDocumentPreparer Preparer { get; }

    ISpoolerPort Spooler { get; }

    IPrinterCatalog Catalog { get; }
}

public sealed class LanSession : IDisposable
{
    private readonly AgentConfig _config;

    public LanSession(
        AgentConfig config,
        AgentDatabase database,
        PairingService pairing,
        JobIntake intake,
        ISecretStore secrets,
        X509Certificate2Material hostCertificate)
    {
        _config = config;
        Database = database;
        Pairing = pairing;
        Intake = intake;
        Secrets = secrets;
        Presented = hostCertificate;
    }

    public AgentDatabase Database { get; }

    public PairingService Pairing { get; }

    public JobIntake Intake { get; }

    public ISecretStore Secrets { get; }

    public X509Certificate2Material Presented { get; private set; }

    public string ExpectedHostName => Presented.HostName;

    public string Fingerprint => HostCertificateTrust.Fingerprint(Presented.Certificate);

    public static LanSession Open(AgentConfig config, ISecretStore secrets, IPrintEngine engine, Func<DateTimeOffset>? now = null, bool recover = true)
    {
        var data = config.ResolveDataDirectory();
        Directory.CreateDirectory(data);
        var database = new AgentDatabase(data);
        if (recover)
        {
            database.Recover();
        }

        database.SyncPublished(DestinationFile.Load(data));
        var pairing = new PairingService(database, secrets, now);
        var hostName = config.ResolveHostName();
        var material = pairing.LoadOrCreateHost(hostName);
        var dispatcher = new PrintDispatcher(database, engine.Catalog, database, engine.Preparer, engine.Spooler);
        var intake = new JobIntake(database, dispatcher, now);
        return new LanSession(config, database, pairing, intake, secrets, material);
    }

    public string IssuePairingCode(TimeSpan? lifetime = null) =>
        Pairing.IssueCode(Fingerprint, ExpectedHostName, lifetime);

    public X509Certificate2Material ReplaceHostCertificate()
    {
        Presented = Pairing.ActivateReplacement(ExpectedHostName);
        return Presented;
    }

    public X509Certificate2Material ConfirmPending(string operatorFingerprint)
    {
        Presented = Pairing.ActivatePending(operatorFingerprint);
        return Presented;
    }

    public async Task<LanHost> ListenAsync(System.Net.IPAddress address, int port, CancellationToken cancellationToken) =>
        await LanHost.StartAsync(this, address, port, cancellationToken).ConfigureAwait(false);

    public void Describe(TextWriter output, string? pairingCode)
    {
        output.WriteLine("NPB Agent en la sesión interactiva.");
        output.WriteLine("Nombre: " + ExpectedHostName);
        output.WriteLine("Huella: " + Fingerprint);
        output.WriteLine("Puerto: " + _config.Port);
        if (!string.IsNullOrWhiteSpace(pairingCode))
        {
            output.WriteLine("Código: " + pairingCode);
            output.WriteLine("El código caduca pronto, se usa una sola vez y queda ligado a esa huella.");
        }
        else
        {
            output.WriteLine("El código de un solo uso se emite con el comando pair en este equipo.");
        }

        output.WriteLine("Esa huella se lee aquí. No se anuncia como dato de confianza por la red.");
    }

    public void Dispose() => Database.Dispose();
}
