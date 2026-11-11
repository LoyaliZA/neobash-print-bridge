using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Neobash.PrintBridge.Core.Dispatch;
using Neobash.PrintBridge.Lan;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
if (args.Length != 0)
{
    var root = args[1];
    var config = new AgentConfig { DataDirectory = root, HostName = "localhost", Port = 0 };
    var secrets = CurrentUserSecretStore.Create(root);
    using var session = LanSession.Open(config, secrets, new FixedCatalog("Test queue", "A4"));
    await using var host = await session.ListenAsync(IPAddress.Loopback, 0, CancellationToken.None);
    if (args[0] == "hold")
    {
        var material = session.Pairing.LoadOrCreateHost("localhost");
        var size = JsonSerializer.SerializeToUtf8Bytes(material.Envelope).Length;
        Check(size > 2560, "PFX envelope exceeds 2560");
        var code = session.IssuePairingCode();
        var paired = session.Pairing.Redeem(code, session.Fingerprint, "probe", null);
        Check(paired.Accepted, "pair");
        var device = session.Database.FindDevice(paired.DeviceId!)!;
        var intake = await session.Intake.AcceptAsync(device, new JobSubmission("probe", "probe", "application/pdf", null, 6),
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1")), CancellationToken.None);
        session.Database.Update(intake.JobId!, JobPhase.Prepared, "interrupted probe");
        File.WriteAllText(session.Database.NewTempPath(), "active sentinel");
        new DpapiSecretStore(root).Put("probe-state", JsonSerializer.SerializeToUtf8Bytes(new State(session.Fingerprint, device.Id, paired.Credential!, intake.JobId!, size)));
        Console.WriteLine("READY");
        await Task.Delay(Timeout.Infinite);
    }
    var state = JsonSerializer.Deserialize<State>(new DpapiSecretStore(root).TryGet("probe-state")!)!;
    Check(state.Pin == session.Fingerprint, "certificate identity persisted across process restart");
    Check(session.Pairing.TryAuthenticate(state.Device, state.Credential, out _), "Credential Manager credential survived restart");
    Check(session.Database.Find(state.Job)!.Phase == JobPhase.Uncertain, "recovery marks interrupted job uncertain");
    Check(!Directory.EnumerateFiles(session.Database.IncomingDirectory).Any(), "recovery removes temporaries");
    var trust = new ClientTrust { DeviceId = state.Device };
    trust.RememberOperatorPin("localhost", host.BaseUri.Port, state.Pin);
    using var client = new LanClient(trust);
    Check((await client.StatusAsync("probe", state.Credential, CancellationToken.None)).Phase == nameof(JobPhase.Uncertain), "TLS and credential work after restart");
    Check((await client.StatusAsync("probe", null, CancellationToken.None)).Transfer == nameof(TransferState.Rechazada), "anonymous status rejected");
    var staged = session.Pairing.StageRenewal("localhost");
    var nextPin = HostCertificateTrust.Fingerprint(staged.Certificate);
    session.ConfirmPending(nextPin);
    Check(new PairingService(session.Database, CurrentUserSecretStore.Create(root)).LoadOrCreateHost("localhost").Certificate.Thumbprint == staged.Certificate.Thumbprint, "rotation persisted");
    var persisted = secrets.TryGet(PairingService.HostSecretName)!;
    try
    {
        secrets.Put(PairingService.HostSecretName, Encoding.UTF8.GetBytes("corrupt certificate envelope"));
        try { session.Pairing.LoadOrCreateHost("localhost"); throw new Exception("corrupt identity silently replaced"); }
        catch (JsonException) { }
    }
    finally { secrets.Put(PairingService.HostSecretName, persisted); }
    session.Pairing.Revoke(state.Device); // remove only this probe's native credential
    Console.WriteLine("RESTART_OK envelopeBytes=" + state.Bytes);
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "npb-process-audit-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
File.WriteAllText(Path.Combine(directory, DestinationFile.FileName), """{"id":"probe","displayName":"Probe","queueName":"Test queue","paperName":"A4"}""");
var name = "npb-audit-" + Guid.NewGuid().ToString("N");
var native = new CredentialManagerSecretStore();
try
{
    var bytes = RandomNumberGenerator.GetBytes(32);
    native.Put(name, bytes);
    Check(native.TryGet(name)!.SequenceEqual(bytes), "native Credential Manager roundtrip");
    try { native.Put(name, new byte[2561]); throw new Exception("oversized blob accepted"); }
    catch (ArgumentException) { }
    var dpapi = new DpapiSecretStore(directory);
    var large = RandomNumberGenerator.GetBytes(8192);
    dpapi.Put(name, large);
    Check(new DpapiSecretStore(directory).TryGet(name)!.SequenceEqual(large), "DPAPI large secret roundtrip");
    using var first = Child("hold", directory);
    try
    {
        while (await first.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40)) is { } line)
        {
            if (line == "READY") break;
        }
        Check(!first.HasExited, "first listener starts: " + (first.HasExited ? first.StandardError.ReadToEnd() : ""));
        using var second = Child("second", directory);
        await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Check(second.ExitCode != 0, "second process fails");
        Check(second.StandardError.ReadToEnd().Contains("Ya hay un agente"), "second rejected by ownership before recovery");
        Check(Directory.EnumerateFiles(Path.Combine(directory, "incoming")).Count() == 1, "second start preserves active temporary");
        using (var db = new AgentDatabase(directory))
        {
            var state = JsonSerializer.Deserialize<State>(new DpapiSecretStore(directory).TryGet("probe-state")!)!;
            Check(db.Find(state.Job)!.Phase == JobPhase.Prepared, "second start does not change active phase");
        }
    }
    finally
    {
        if (!first.HasExited) first.Kill(entireProcessTree: true);
        await first.WaitForExitAsync();
    }
    using var restarted = Child("restart", directory);
    var result = await restarted.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(40));
    await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
    Check(restarted.ExitCode == 0, "restart succeeds: " + restarted.StandardError.ReadToEnd());
    Console.WriteLine(result.Trim());
    Console.WriteLine("PASS: native secrets, certificate persistence/rotation, two processes, crash recovery; no printer opened.");
    Console.WriteLine("Evidence directory: " + directory);
}
finally { native.Delete(name); }

static Process Child(string mode, string root)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    info.ArgumentList.Add(mode); info.ArgumentList.Add(root);
    return Process.Start(info)!;
}
static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
record State(string Pin, string Device, string Credential, string Job, int Bytes);
