using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Neobash.PrintBridge.Lan;

public sealed class ClientTrust
{
    public const string CredentialSecretName = "npb-client-credential";

    public string HostName { get; set; } = "";

    public int Port { get; set; }

    public string Fingerprint { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public void ObserveUnauthenticatedAnnouncement(string? fingerprint, string? hostName)
    {
        _ = fingerprint;
        _ = hostName;
    }

    public void RememberOperatorPin(string hostName, int port, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("Falta la huella. Hay que escribirla desde el equipo anfitrión, no desde la conexión.");
        }

        HostName = hostName.Trim();
        Port = port;
        Fingerprint = fingerprint.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
    }

    public static ClientTrust Load(string directory)
    {
        var path = Path.Combine(directory, "client-trust.json");
        if (!File.Exists(path))
        {
            return new ClientTrust();
        }

        return JsonSerializer.Deserialize<ClientTrust>(File.ReadAllText(path)) ?? new ClientTrust();
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "client-trust.json"), JsonSerializer.Serialize(this));
    }
}

public sealed class LanClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ClientTrust _trust;

    public LanClient(ClientTrust trust)
    {
        if (string.IsNullOrWhiteSpace(trust.Fingerprint))
        {
            throw new ArgumentException("Falta la huella. Hay que escribirla desde el equipo anfitrión, no desde la conexión.");
        }

        _trust = trust;
        var handler = new SocketsHttpHandler
        {
            SslOptions =
            {
                RemoteCertificateValidationCallback = Validate,
            },
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{trust.HostName}:{trust.Port}/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    public string PinnedFingerprint => _trust.Fingerprint;

    public string? IgnoredAnnouncement { get; private set; }

    public string? LastTlsFailure { get; private set; }

    public void ObserveUnauthenticatedAnnouncement(string? fingerprint, string? hostName)
    {
        IgnoredAnnouncement = fingerprint;
        _trust.ObserveUnauthenticatedAnnouncement(fingerprint, hostName);
    }

    public void UseOperatorFingerprint(string fingerprint) => _trust.RememberOperatorPin(_trust.HostName, _trust.Port, fingerprint);

    public async Task<PairWire> PairAsync(string code, string? displayName, string? previousDeviceId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, "v1/pair", JsonContent(("code", code), ("displayName", displayName), ("previousDeviceId", previousDeviceId)), null, cancellationToken).ConfigureAwait(false);
        var wire = await ReadAsync<PairWire>(response, cancellationToken).ConfigureAwait(false);
        ObserveUnauthenticatedAnnouncement(wire.Fingerprint, wire.HostName);
        return wire;
    }

    public async Task<TransferWire> SendAsync(string destinationId, string sourceId, Stream pdf, string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/jobs")
        {
            Content = new StreamContent(pdf),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        request.Headers.TryAddWithoutValidation("X-Npb-Destination-Id", destinationId);
        request.Headers.TryAddWithoutValidation("X-Npb-Source-Id", sourceId);
        AddAuth(request, credential);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TransferWire>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TransferWire> StatusAsync(string sourceId, string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/jobs/" + Uri.EscapeDataString(sourceId));
        AddAuth(request, credential);
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TransferWire>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DestinationsAsync(string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/destinations");
        AddAuth(request, credential);
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfirmWire> ConfirmAsync(string operatorFingerprint, string? credential, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "v1/certificate/confirm",
            JsonContent(("fingerprint", operatorFingerprint)),
            credential,
            cancellationToken).ConfigureAwait(false);
        var wire = await ReadAsync<ConfirmWire>(response, cancellationToken).ConfigureAwait(false);
        ObserveUnauthenticatedAnnouncement(wire.Fingerprint, null);
        return wire;
    }

    public void Dispose() => _http.Dispose();

    private bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        var typed = certificate as X509Certificate2 ?? (certificate is null ? null : new X509Certificate2(certificate));
        var statuses = chain?.ChainStatus.Select(status => status.Status.ToString()) ?? [];
        var allowed = HostCertificateTrust.Allow(typed, chain, errors, _trust.HostName, _trust.HostName, _trust.Fingerprint, DateTimeOffset.UtcNow);
        if (!allowed)
        {
            LastTlsFailure = errors + " " + string.Join(",", statuses);
        }

        if (typed is not null && certificate is not X509Certificate2)
        {
            typed.Dispose();
        }

        return allowed;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent content, string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        AddAuth(request, credential);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void AddAuth(HttpRequestMessage request, string? credential)
    {
        if (!string.IsNullOrWhiteSpace(_trust.DeviceId) && !string.IsNullOrWhiteSpace(credential))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Npb " + _trust.DeviceId + " " + credential);
        }
    }

    private static HttpContent JsonContent(params (string Name, string? Value)[] fields)
    {
        var payload = fields.Where(field => field.Value is not null).ToDictionary(field => field.Name, field => field.Value);
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("La respuesta no trae un cuerpo reconocible.");
    }
}

public sealed class PairWire
{
    public string? DeviceId { get; set; }

    public string? Credential { get; set; }

    public string? DestinationId { get; set; }

    public string? DestinationName { get; set; }

    public string? Detail { get; set; }

    public string? Fingerprint { get; set; }

    public string? HostName { get; set; }
}

public sealed class TransferWire
{
    public string? Transfer { get; set; }

    public string? JobId { get; set; }

    public string? Phase { get; set; }

    public string? Detail { get; set; }
}

public sealed class ConfirmWire
{
    public string? Credential { get; set; }

    public string? Detail { get; set; }

    public string? Fingerprint { get; set; }
}
