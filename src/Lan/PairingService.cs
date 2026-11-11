using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Neobash.PrintBridge.Lan;

public sealed record PairResult(bool Accepted, string Detail, string? DeviceId, string? Credential, string? DestinationId, string? DestinationName);

public sealed class PairingService
{
    public const string HostSecretName = "npb-host-certificate";

    public const string PendingSecretName = "npb-host-certificate-pending";

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly AgentDatabase _database;
    private readonly ISecretStore _secrets;
    private readonly Func<DateTimeOffset> _now;

    public PairingService(AgentDatabase database, ISecretStore secrets, Func<DateTimeOffset>? now = null)
    {
        _database = database;
        _secrets = secrets;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string IssueCode(string fingerprint, string expectedHostName, TimeSpan? lifetime = null)
    {
        var code = NewCode();
        _database.IssueCode(Hash(code), fingerprint, expectedHostName, _now().Add(lifetime ?? TimeSpan.FromMinutes(10)));
        _database.Audit("emparejamiento", null, null, null, null, null, null, "emitido");
        return code;
    }

    public PairResult Redeem(string code, string presentedFingerprint, string? displayName, string? previousDeviceId)
    {
        var destinations = ((Neobash.PrintBridge.Core.Dispatch.IDestinationStore)_database).List();
        if (destinations.Count != 1)
        {
            _database.Audit("emparejamiento", null, null, null, null, null, null, "destino");
            return new PairResult(false, "El anfitrión no tiene un único destino publicado.", null, null, null, null);
        }

        var consumption = _database.ConsumeCode(Hash(code), presentedFingerprint, _now());
        if (consumption.Status != CodeStatus.Accepted)
        {
            var failure = consumption.Status switch
            {
                CodeStatus.Used => "reutilizado",
                CodeStatus.Expired => "caducado",
                CodeStatus.FingerprintMismatch => "huella",
                _ => "desconocido",
            };
            _database.Audit("emparejamiento", null, null, null, null, null, null, failure);
            var detail = consumption.Status switch
            {
                CodeStatus.Used => "El código ya se usó.",
                CodeStatus.Expired => "El código caducó.",
                CodeStatus.FingerprintMismatch => "El código no corresponde a la huella verificada.",
                _ => "El código no es válido.",
            };
            return new PairResult(false, detail, null, null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(previousDeviceId))
        {
            Revoke(previousDeviceId);
        }

        var destination = destinations[0];
        var credentialName = "device-" + Guid.NewGuid().ToString("N");
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _secrets.Put(credentialName, Encoding.UTF8.GetBytes(credential));
        var device = _database.InsertDevice(
            string.IsNullOrWhiteSpace(displayName) ? "Equipo cliente" : displayName.Trim(),
            credentialName,
            presentedFingerprint,
            consumption.ExpectedHostName ?? "",
            destination.Id,
            _now());
        _database.Audit("emparejamiento", device.Id, destination.Id, null, null, null, null, "aceptado");
        return new PairResult(true, "El equipo quedó vinculado.", device.Id, credential, destination.Id, destination.DisplayName);
    }

    public bool TryAuthenticate(string deviceId, string credential, out DeviceRecord? device)
    {
        device = _database.FindDevice(deviceId);
        if (device is null || device.Revoked)
        {
            _database.Audit("autenticacion", deviceId, null, null, null, null, null, "rechazada");
            device = null;
            return false;
        }

        var stored = _secrets.TryGet(device.CredentialName);
        if (stored is null || !SecretEquals(Encoding.UTF8.GetString(stored), credential))
        {
            _database.Audit("autenticacion", deviceId, null, null, null, null, null, "rechazada");
            device = null;
            return false;
        }

        _database.Audit("autenticacion", deviceId, null, null, null, null, null, "aceptada");
        return true;
    }

    public bool Revoke(string deviceId)
    {
        var device = _database.FindDevice(deviceId);
        if (device is null)
        {
            return false;
        }

        _database.Revoke(deviceId, _now());
        _secrets.Delete(device.CredentialName);
        _database.Audit("revocacion", deviceId, null, null, null, null, null, "revocado");
        return true;
    }

    public string? RotateCredential(string deviceId, string fingerprint, string expectedHostName)
    {
        var device = _database.FindDevice(deviceId);
        if (device is null || device.Revoked)
        {
            return null;
        }

        var credentialName = "device-" + Guid.NewGuid().ToString("N");
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _secrets.Put(credentialName, Encoding.UTF8.GetBytes(credential));
        _database.RotateCredential(deviceId, credentialName, fingerprint, expectedHostName);
        _secrets.Delete(device.CredentialName);
        _database.Audit("credencial", deviceId, null, null, null, null, null, "rotada");
        return credential;
    }

    public X509Certificate2Material LoadOrCreateHost(string hostName)
    {
        var existing = ReadMaterial(HostSecretName);
        if (existing is not null)
        {
            return existing;
        }

        if (_database.HasDevices())
            throw new InvalidOperationException("Falta el certificado de una identidad con dispositivos vinculados. Restaure el almacén; no se regeneró el certificado.");
        var created = X509Certificate2Material.Create(hostName, _now().AddMinutes(-1), _now().AddDays(365));
        WriteMaterial(HostSecretName, created);
        return created;
    }

    public X509Certificate2Material StageRenewal(string hostName)
    {
        var created = X509Certificate2Material.Create(hostName, _now().AddMinutes(-1), _now().AddDays(365));
        WriteMaterial(PendingSecretName, created);
        _database.Audit("certificado", null, null, null, null, null, null, "preparado");
        return created;
    }

    public X509Certificate2Material? PendingHost() => ReadMaterial(PendingSecretName);

    public X509Certificate2Material ActivatePending(string operatorFingerprint)
    {
        var current = ReadMaterial(HostSecretName) ?? throw new InvalidOperationException("No hay certificado vigente.");
        if (!HostCertificateTrust.IsCurrent(current.Certificate, _now()))
        {
            _database.Audit("certificado", null, null, null, null, null, null, "caducado");
            throw new InvalidOperationException("Un certificado caducado no autoriza su reemplazo.");
        }

        var pending = ReadMaterial(PendingSecretName) ?? throw new InvalidOperationException("No hay un certificado nuevo preparado.");
        if (!string.Equals(HostCertificateTrust.Fingerprint(pending.Certificate), Normalize(operatorFingerprint), StringComparison.OrdinalIgnoreCase))
        {
            _database.Audit("certificado", null, null, null, null, null, null, "huella");
            throw new InvalidOperationException("La huella confirmada no es la del certificado preparado.");
        }

        WriteMaterial(HostSecretName, pending);
        _secrets.Delete(PendingSecretName);
        _database.Audit("certificado", null, null, null, null, null, null, "activado");
        return pending;
    }

    public X509Certificate2Material ActivateReplacement(string hostName)
    {
        var created = X509Certificate2Material.Create(hostName, _now().AddMinutes(-1), _now().AddDays(365));
        WriteMaterial(HostSecretName, created);
        _secrets.Delete(PendingSecretName);
        _database.Audit("certificado", null, null, null, null, null, null, "reemplazado");
        return created;
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    public static bool SecretEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        if (a.Length != b.Length)
        {
            CryptographicOperations.FixedTimeEquals(a, a);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private X509Certificate2Material? ReadMaterial(string name)
    {
        var bytes = _secrets.TryGet(name);
        if (bytes is null)
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<CertificateEnvelope>(bytes) ?? throw new InvalidOperationException("El certificado guardado no se puede leer.");
        return X509Certificate2Material.Load(envelope);
    }

    private void WriteMaterial(string name, X509Certificate2Material material) =>
        _secrets.Put(name, JsonSerializer.SerializeToUtf8Bytes(material.Envelope));

    private static string NewCode()
    {
        var chars = new char[10];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    private static string Normalize(string fingerprint) =>
        fingerprint.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
}

public sealed class X509Certificate2Material
{
    private X509Certificate2Material(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, CertificateEnvelope envelope)
    {
        Certificate = certificate;
        Envelope = envelope;
    }

    public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate { get; }

    public CertificateEnvelope Envelope { get; }

    public string HostName => Envelope.HostName;

    public static X509Certificate2Material Create(string hostName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var certificate = HostCertificateTrust.CreateSelfSigned(hostName, notBefore, notAfter);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password);
        var envelope = new CertificateEnvelope(hostName, password, Convert.ToBase64String(pfx));
        certificate.Dispose();
        return Load(envelope);
    }

    public static X509Certificate2Material Load(CertificateEnvelope envelope)
    {
        var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(envelope.Pfx),
            envelope.Password,
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);
        return new X509Certificate2Material(certificate, envelope);
    }
}

public sealed record CertificateEnvelope(string HostName, string Password, string Pfx);
