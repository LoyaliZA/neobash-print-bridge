using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Lan;

namespace Neobash.PrintBridge.Tests;

public sealed class LanProtocolTests
{
    [Fact]
    public void Unauthenticated_announcement_does_not_become_the_pin()
    {
        var trust = new ClientTrust();
        trust.RememberOperatorPin("print-host", 8741, "AA");
        trust.ObserveUnauthenticatedAnnouncement("BB", "other-host");
        var client = new ClientTrust { HostName = "print-host", Port = 8741, Fingerprint = "AA" };
        client.ObserveUnauthenticatedAnnouncement("CC", "socket-host");
        Assert.Equal("AA", trust.Fingerprint);
        Assert.Equal("print-host", trust.HostName);
        Assert.Equal("AA", client.Fingerprint);
        Assert.Equal("print-host", client.HostName);
    }

    [Fact]
    public void Pairing_code_is_single_use_bound_to_the_fingerprint_and_expiry()
    {
        using var fixture = Fixture.Open();
        var code = fixture.Session.IssuePairingCode(TimeSpan.FromMinutes(5));
        var wrong = fixture.Session.Pairing.Redeem(code, "00", "Cliente", null);
        Assert.False(wrong.Accepted);
        Assert.Contains("huella", wrong.Detail, StringComparison.OrdinalIgnoreCase);
        var expired = Fixture.Open(now: () => DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var oldCode = expired.Session.IssuePairingCode(TimeSpan.FromMinutes(5));
        expired.Dispose();
        using var later = Fixture.Open(now: () => DateTimeOffset.Parse("2026-01-01T00:06:00Z"), root: expired.Root, secrets: expired.Secrets);
        var stale = later.Session.Pairing.Redeem(oldCode, later.Session.Fingerprint, "Cliente", null);
        Assert.False(stale.Accepted);
        Assert.Contains("caduc", stale.Detail, StringComparison.OrdinalIgnoreCase);
        var again = fixture.Session.Pairing.Redeem(code, fixture.Session.Fingerprint, "Cliente", null);
        Assert.True(again.Accepted);
        var reuse = fixture.Session.Pairing.Redeem(code, fixture.Session.Fingerprint, "Cliente", null);
        Assert.False(reuse.Accepted);
        Assert.Contains("usó", reuse.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(code, fixture.Session.Database.ReadAuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Certificate_checks_reject_every_tls_error_except_untrusted_root_of_the_pinned_certificate()
    {
        var now = DateTimeOffset.UtcNow;
        using var good = HostCertificateTrust.CreateSelfSigned("print-host", now.AddMinutes(-5), now.AddDays(30));
        var pin = HostCertificateTrust.Fingerprint(good);
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(good);
        var allowed = HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "print-host", "print-host", pin, now);
        Assert.True(allowed, string.Join(" | ", chain.ChainStatus.Select(status => status.Status + " " + status.StatusInformation))
            + " usage=" + HostCertificateTrust.HasServerUsage(good)
            + " current=" + HostCertificateTrust.IsCurrent(good, now)
            + " names=" + string.Join(",", HostCertificateTrust.Names(good))
            + " identity=" + HostCertificateTrust.IdentityMatches(good, "print-host")
            + " pin=" + HostCertificateTrust.FingerprintEquals(good, pin));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.None, "print-host", "print-host", new string('A', 64), now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateNameMismatch, "print-host", "print-host", pin, now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateNotAvailable, "print-host", "print-host", pin, now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch, "print-host", "print-host", pin, now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "otro", "otro", pin, now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "print-host", "127.0.0.1", pin, now));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "print-host", "print-host", pin, now.AddMinutes(-10)));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "print-host", "print-host", pin, now.AddDays(31)));
        Assert.False(HostCertificateTrust.ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.Revoked]));
        Assert.False(HostCertificateTrust.ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.NotTimeValid]));
        Assert.False(HostCertificateTrust.ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.OfflineRevocation]));
        Assert.False(HostCertificateTrust.ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.PartialChain]));
        Assert.True(HostCertificateTrust.ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.UntrustedRoot]));
        Assert.True(new Func<SslPolicyErrors, bool>(_ => true)(SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(HostCertificateTrust.Allow(good, chain, SslPolicyErrors.RemoteCertificateChainErrors, "print-host", "print-host", new string('D', 64), now));

        using var noUsage = Issue(request => { }, "print-host", now);
        Assert.False(HostCertificateTrust.HasServerUsage(noUsage));
        using var clientOnly = Issue(request =>
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var oids = new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, true));
        }, "print-host", now);
        Assert.False(HostCertificateTrust.HasServerUsage(clientOnly));
        using var extra = Issue(request =>
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var oids = new OidCollection { new Oid(HostCertificateTrust.ServerAuthenticationOid), new Oid("1.3.6.1.5.5.7.3.2") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, true));
        }, "print-host", now);
        Assert.False(HostCertificateTrust.HasServerUsage(extra));
    }

    [Fact]
    public async Task Accepted_pdf_dispatches_once_and_a_repeat_returns_the_stored_phase()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var first = await fixture.Send(device, "origen1", Pdf());
        Assert.Equal(TransferState.Aceptada, first.Transfer);
        Assert.Equal(JobPhase.SubmittedToSpooler, first.Phase);
        Assert.Equal(1, fixture.Spooler.Calls);
        Assert.True(fixture.Spooler.SawCommittedJob);
        Assert.Contains("no confirma el papel", first.Detail, StringComparison.OrdinalIgnoreCase);
        var second = await fixture.Send(device, "origen1", Pdf());
        Assert.Equal(TransferState.Aceptada, second.Transfer);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(JobPhase.SubmittedToSpooler, second.Phase);
        Assert.Equal(1, fixture.Spooler.Calls);
    }

    [Fact]
    public async Task Concurrent_same_key_commits_and_dispatches_once()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var body = Pdf();
        var results = await Task.WhenAll(
            fixture.Send(device, "origen2", body),
            fixture.Send(device, "origen2", body.ToArray()));
        Assert.Equal(1, fixture.Spooler.Calls);
        Assert.All(results, result => Assert.Equal(TransferState.Aceptada, result.Transfer));
        Assert.Single(fixture.Session.Database.ListIds());
        Assert.Equal(results[0].JobId, results[1].JobId);
    }

    [Fact]
    public async Task Concurrent_same_key_with_another_body_or_destination_conflicts_without_a_second_dispatch()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        fixture.Session.Database.EnsurePublished(new PublishedDestination("otro", "Otro", "Cola fisica", "A4"));
        fixture.Session.Database.Grant(device.Id, "otro");
        var results = await Task.WhenAll(
            fixture.Send(device, "origen3", Pdf(), "mostrador"),
            fixture.Send(device, "origen3", Encoding.ASCII.GetBytes("%PDF-1.4\nother\n"), "otro"));
        Assert.InRange(fixture.Spooler.Calls, 0, 1);
        Assert.Equal(1, fixture.Spooler.Calls);
        Assert.Single(fixture.Session.Database.ListIds());
        Assert.Contains(results, result => result.Transfer == TransferState.Aceptada);
        Assert.Contains(results, result => result.Transfer == TransferState.Conflicto);
        var stored = fixture.Session.Database.Find(results.Single(result => result.Transfer == TransferState.Aceptada).JobId!);
        Assert.NotNull(stored);
        var other = await fixture.Send(device, "origen4", Pdf(), "otro");
        Assert.Equal(TransferState.Aceptada, other.Transfer);
        var conflict = await fixture.Send(device, "origen4", Pdf(), "mostrador");
        Assert.Equal(TransferState.Conflicto, conflict.Transfer);
        Assert.Equal(2, fixture.Spooler.Calls);
        Assert.Equal("otro", fixture.Session.Database.Find(other.JobId!)!.DestinationId);
    }

    [Fact]
    public async Task Interrupted_transfer_leaves_no_job_and_a_later_full_body_is_accepted_once()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var broken = await fixture.Session.Intake.AcceptAsync(device, new JobSubmission("origen5", "mostrador", "application/pdf", null, 40), new CuttingStream(12), CancellationToken.None);
        Assert.Equal(TransferState.Rechazada, broken.Transfer);
        Assert.Empty(fixture.Session.Database.ListIds());
        Assert.Empty(Directory.EnumerateFiles(fixture.Session.Database.IncomingDirectory));
        Assert.Equal(0, fixture.Spooler.Calls);
        var accepted = await fixture.Send(device, "origen5", Pdf());
        Assert.Equal(TransferState.Aceptada, accepted.Transfer);
        Assert.Equal(1, fixture.Spooler.Calls);
    }

    [Fact]
    public void Startup_deletes_temps_and_orphans_and_does_not_dispatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "npb-lan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "incoming"));
        Directory.CreateDirectory(Path.Combine(root, "documents"));
        File.WriteAllBytes(Path.Combine(root, "incoming", "t1.partial"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(root, "documents", "orphan.pdf"), Encoding.ASCII.GetBytes("%PDF-orphan"));
        File.WriteAllText(Path.Combine(root, "destination.local.json"), DestinationJson);
        var engine = new FixedCatalog("Cola fisica", "A4");
        using var session = LanSession.Open(new AgentConfig { DataDirectory = root, HostName = "localhost" }, new MemorySecretStore(), engine);
        Assert.Empty(Directory.EnumerateFiles(session.Database.IncomingDirectory));
        Assert.Empty(Directory.EnumerateFiles(session.Database.DocumentsDirectory));
        Assert.Equal(0, engine.Counting.Calls);
    }

    [Fact]
    public void Side_open_keeps_an_incoming_transfer_until_the_listener_restarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "npb-lan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "destination.local.json"), DestinationJson);
        var secrets = new MemorySecretStore();
        var config = new AgentConfig { DataDirectory = root, HostName = "localhost" };
        using var listening = LanSession.Open(config, secrets, new FixedCatalog("Cola fisica", "A4"));
        var temp = Path.Combine(listening.Database.IncomingDirectory, "t-active.partial");
        File.WriteAllBytes(temp, [9]);
        using var side = LanSession.Open(config, secrets, new FixedCatalog("Cola fisica", "A4"), recover: false);
        Assert.True(File.Exists(temp));
        side.Dispose();
        listening.Dispose();
        using var restarted = LanSession.Open(config, secrets, new FixedCatalog("Cola fisica", "A4"));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public async Task Received_and_prepared_become_uncertain_and_are_not_printed_again()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var accepted = await fixture.Send(device, "origen6", Pdf());
        fixture.Session.Database.Update(accepted.JobId!, JobPhase.Prepared, "preparado");
        var root = fixture.Root;
        var secrets = fixture.Secrets;
        fixture.Dispose();
        var engine = new FixedCatalog("Cola fisica", "A4");
        using var restarted = LanSession.Open(new AgentConfig { DataDirectory = root, HostName = "localhost" }, secrets, engine);
        var record = restarted.Database.Find(accepted.JobId!);
        Assert.Equal(JobPhase.Uncertain, record!.Phase);
        Assert.Equal(JobRecovery.InterruptedDetail, record.Detail);
        Assert.Equal(0, engine.Counting.Calls);
        Assert.Throws<InvalidOperationException>(() => restarted.Database.RecordPhysicalConfirmation(accepted.JobId!, "se vio"));
        restarted.Database.Update(accepted.JobId!, JobPhase.SubmittedToSpooler, "El spooler aceptó el trabajo. Eso no confirma el papel.");
        restarted.Database.RecordPhysicalConfirmation(accepted.JobId!, "quien vio la hoja");
        Assert.Equal(JobPhase.SubmittedToSpooler, restarted.Database.Find(accepted.JobId!)!.Phase);
        restarted.Dispose();
        using var again = LanSession.Open(new AgentConfig { DataDirectory = root, HostName = "localhost" }, secrets, new FixedCatalog("Cola fisica", "A4"));
        var survived = again.Database.Find(accepted.JobId!)!;
        Assert.Equal(JobPhase.SubmittedToSpooler, survived.Phase);
        Assert.Equal("quien vio la hoja", survived.PhysicalConfirmation);
    }

    [Fact]
    public async Task Unsafe_source_queue_name_unknown_destination_and_non_pdf_create_nothing()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        foreach (var source in new[] { "..", "../fuera", "a/b", "a\\b" })
        {
            var rejected = await fixture.Send(device, source, Pdf());
            Assert.Equal(TransferState.Rechazada, rejected.Transfer);
        }

        var queue = await fixture.Session.Intake.AcceptAsync(device, new JobSubmission("origen7", "mostrador", "application/pdf", "Cola fisica", null), new MemoryStream(Pdf()), CancellationToken.None);
        Assert.Equal(TransferState.Rechazada, queue.Transfer);
        var missing = await fixture.Send(device, "origen8", Pdf(), "no-publicado");
        Assert.Equal(TransferState.Rechazada, missing.Transfer);
        var oxps = await fixture.Session.Intake.AcceptAsync(device, new JobSubmission("origen9", "mostrador", "application/oxps", null, null), new MemoryStream(Pdf()), CancellationToken.None);
        Assert.Equal(TransferState.Rechazada, oxps.Transfer);
        Assert.Empty(fixture.Session.Database.ListIds());
        Assert.Empty(Directory.EnumerateFiles(fixture.Session.Database.IncomingDirectory));
        Assert.Equal(0, fixture.Spooler.Calls);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "destinations.json")));
    }

    [Fact]
    public async Task Oversized_body_and_rate_limit_leave_no_key()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var oversized = await fixture.Session.Intake.AcceptAsync(
            device,
            new JobSubmission("grande", "mostrador", "application/pdf", null, null),
            new ZeroStream(AgentDatabase.MaxBodyBytes + 1),
            CancellationToken.None);
        Assert.Equal(TransferState.Rechazada, oversized.Transfer);
        Assert.Empty(Directory.EnumerateFiles(fixture.Session.Database.IncomingDirectory));
        Assert.Null(fixture.Session.Database.FindIdempotency(device.Id, "grande"));
        for (var index = 0; index < AgentDatabase.MaxAcceptsPerWindow; index++)
        {
            var accepted = await fixture.Send(device, "lote" + index, Pdf());
            Assert.Equal(TransferState.Aceptada, accepted.Transfer);
        }

        var limited = await fixture.Send(device, "lote-extra", Pdf());
        Assert.Equal(TransferState.Rechazada, limited.Transfer);
        Assert.Null(fixture.Session.Database.FindIdempotency(device.Id, "lote-extra"));
        Assert.Empty(Directory.EnumerateFiles(fixture.Session.Database.IncomingDirectory));
        Assert.Equal(AgentDatabase.MaxAcceptsPerWindow, fixture.Spooler.Calls);
    }

    [Fact]
    public async Task Revocation_keeps_history_and_blocks_the_next_request()
    {
        using var fixture = Fixture.Open();
        var device = fixture.Pair();
        var accepted = await fixture.Send(device, "origen10", Pdf());
        Assert.True(fixture.Session.Pairing.Revoke(device.Id));
        var revoked = fixture.Session.Database.FindDevice(device.Id);
        Assert.True(revoked!.Revoked);
        Assert.NotNull(fixture.Session.Database.Find(accepted.JobId!));
        var blocked = await fixture.Send(revoked, "origen11", Pdf());
        Assert.Equal(TransferState.Rechazada, blocked.Transfer);
        Assert.Equal(1, fixture.Spooler.Calls);
    }

    [Fact]
    public async Task Http_client_uses_the_operator_pin_and_one_in_flight_transfer()
    {
        using var fixture = Fixture.Open();
        await using var host = await fixture.Session.ListenAsync(IPAddress.Loopback, 0, CancellationToken.None);
        var trust = new ClientTrust();
        trust.RememberOperatorPin("localhost", host.BaseUri.Port, fixture.Session.Fingerprint);
        using var client = new LanClient(trust);
        client.ObserveUnauthenticatedAnnouncement(new string('B', 64), "anunciado");
        Assert.Equal(fixture.Session.Fingerprint, client.PinnedFingerprint);
        var wrong = new ClientTrust();
        wrong.RememberOperatorPin("localhost", host.BaseUri.Port, new string('C', 64));
        using var rejected = new LanClient(wrong);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => rejected.PairAsync("NO-CODE", "Cliente", null, CancellationToken.None));
        Assert.Equal(new string('C', 64), rejected.PinnedFingerprint);
        var code = fixture.Session.IssuePairingCode();
        var paired = await client.PairAsync(code, "Cliente", null, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(paired.DeviceId));
        Assert.DoesNotContain(code, fixture.Session.Database.ReadAuditText(), StringComparison.Ordinal);
        Assert.True(File.ReadAllBytes(fixture.Session.Database.DatabasePath).AsSpan().IndexOf(Encoding.UTF8.GetBytes(paired.Credential!)) < 0);
        trust.DeviceId = paired.DeviceId!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Spooler.BeforeReturn = async _ =>
        {
            started.TrySetResult();
            await release.Task;
        };
        var first = client.SendAsync(paired.DestinationId!, "http1", new MemoryStream(Pdf()), paired.Credential, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await client.SendAsync(paired.DestinationId!, "http2", new MemoryStream(Pdf()), paired.Credential, CancellationToken.None);
        Assert.Equal(nameof(TransferState.Rechazada), second.Transfer);
        release.TrySetResult();
        var completed = await first;
        Assert.Equal(nameof(TransferState.Aceptada), completed.Transfer);
        Assert.Equal(nameof(JobPhase.SubmittedToSpooler), completed.Phase);
        Assert.Equal(1, fixture.Spooler.Calls);
        var repeat = await client.SendAsync(paired.DestinationId!, "http1", new MemoryStream(Pdf()), paired.Credential, CancellationToken.None);
        Assert.Equal(nameof(TransferState.Aceptada), repeat.Transfer);
        Assert.Equal(1, fixture.Spooler.Calls);
        var listed = await client.DestinationsAsync(paired.Credential, CancellationToken.None);
        Assert.Contains("mostrador", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("Cola fisica", listed, StringComparison.Ordinal);
        var marker = Encoding.ASCII.GetBytes("%PDF-MARCADOR-SECRETO");
        await client.SendAsync(paired.DestinationId!, "http3", new MemoryStream(marker), paired.Credential, CancellationToken.None);
        var audit = fixture.Session.Database.ReadAuditText();
        Assert.DoesNotContain("MARCADOR-SECRETO", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(paired.Credential!, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(code, audit, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_and_library_projects_do_not_host_WinUI_or_desktop()
    {
        var root = RepositoryRoot();
        var agent = File.ReadAllText(Path.Combine(root, "src", "Agent", "Neobash.PrintBridge.Agent.csproj"));
        var library = File.ReadAllText(Path.Combine(root, "src", "Windows", "PrintEngine", "Neobash.PrintBridge.PrintEngine.csproj"));
        var lan = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "Lan"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("net10.0-windows10.0.26100.0", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWinUI", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("Desktop.csproj", agent, StringComparison.Ordinal);
        Assert.Contains("third-party\\pdfium", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWinUI", library, StringComparison.Ordinal);
        Assert.DoesNotContain("destinations.json", lan, StringComparison.Ordinal);
        Assert.DoesNotContain("FileJobRecordStore", lan, StringComparison.Ordinal);
        Assert.DoesNotContain("DangerousAcceptAnyServerCertificateValidator", lan, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_account_secret_store_needs_Windows_on_this_host()
    {
        var directory = Path.Combine(Path.GetTempPath(), "npb-secrets-" + Guid.NewGuid().ToString("N"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(CredentialManagerSecretStore.IsSupported);
            Assert.Throws<PlatformNotSupportedException>(() => CurrentUserSecretStore.Create(directory));
            return;
        }

        var store = CurrentUserSecretStore.Create(directory);
        Assert.True(store is CredentialManagerSecretStore || store is DpapiSecretStore);
    }

    [Fact]
    public async Task Client_command_refuses_to_pair_without_an_operator_fingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "npb-client-" + Guid.NewGuid().ToString("N"));
        var error = new StringWriter();
        var code = await ClientConsole.RunAsync(["pair", "--host", "localhost", "--port", "9", "--code", "ABCDE"], TextWriter.Null, error, new MemorySecretStore(), root);
        Assert.Equal(2, code);
        Assert.Contains("huella", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2 Issue(Action<CertificateRequest> extend, string hostName, DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        extend(request);
        using var issued = request.CreateSelfSigned(now.AddMinutes(-1), now.AddDays(1));
        var pfx = issued.Export(X509ContentType.Pfx, "test");
        return X509CertificateLoader.LoadPkcs12(pfx, "test", X509KeyStorageFlags.Exportable);
    }

    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Neobash.PrintBridge.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    private const string DestinationJson = """
        {"id":"mostrador","displayName":"Mostrador","queueName":"Cola fisica","paperName":"A4"}
        """;

    private sealed class Fixture : IDisposable
    {
        private Fixture(LanSession session, CountingSpooler spooler, string root, MemorySecretStore secrets)
        {
            Session = session;
            Spooler = spooler;
            Root = root;
            Secrets = secrets;
        }

        public LanSession Session { get; }

        public CountingSpooler Spooler { get; }

        public string Root { get; }

        public MemorySecretStore Secrets { get; }

        public static Fixture Open(Func<DateTimeOffset>? now = null, string? root = null, MemorySecretStore? secrets = null)
        {
            root ??= Path.Combine(Path.GetTempPath(), "npb-lan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var destination = Path.Combine(root, "destination.local.json");
            if (!File.Exists(destination))
            {
                File.WriteAllText(destination, DestinationJson);
            }

            secrets ??= new MemorySecretStore();
            var engine = new FixedCatalog("Cola fisica", "A4");
            var session = LanSession.Open(new AgentConfig { DataDirectory = root, HostName = "localhost", Port = 8741 }, secrets, engine, now);
            engine.Counting.CommitVisible = id => session.Database.Find(id) is not null;
            return new Fixture(session, engine.Counting, root, secrets);
        }

        public DeviceRecord Pair()
        {
            var code = Session.IssuePairingCode();
            var paired = Session.Pairing.Redeem(code, Session.Fingerprint, "Cliente", null);
            Assert.True(paired.Accepted, paired.Detail);
            return Session.Database.FindDevice(paired.DeviceId!)!;
        }

        public Task<IntakeResult> Send(DeviceRecord device, string source, byte[] body, string destination = "mostrador") =>
            Session.Intake.AcceptAsync(device, new JobSubmission(source, destination, "application/pdf", null, body.Length), new MemoryStream(body), CancellationToken.None);

        public void Dispose() => Session.Dispose();
    }

    private sealed class CuttingStream(int allowed) : Stream
    {
        private int _left = allowed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left == 0)
            {
                throw new IOException("corte");
            }

            var take = Math.Min(count, _left);
            _left -= take;
            return take;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ZeroStream : Stream
    {
        private readonly long _declared;
        private long _left;

        public ZeroStream(long length)
        {
            _declared = length;
            _left = length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _declared;
        public override long Position { get => _declared - _left; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left == 0)
            {
                return 0;
            }

            var take = (int)Math.Min(count, _left);
            _left -= take;
            return take;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
