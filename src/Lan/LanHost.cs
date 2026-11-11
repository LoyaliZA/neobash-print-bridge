using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace Neobash.PrintBridge.Lan;

public sealed class LanHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes = new(StringComparer.Ordinal);

    private LanHost(WebApplication app, Uri baseUri)
    {
        _app = app;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; private set; }

    public static async Task<LanHost> StartAsync(LanSession session, IPAddress address, int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = AgentDatabase.MaxBodyBytes + 1024;
            options.Listen(address, port, listen =>
            {
                listen.UseHttps(https =>
                {
                    https.ServerCertificateSelector = (_, _) => session.Presented.Certificate;
                });
            });
        });

        var app = builder.Build();
        var host = new LanHost(app, new Uri("https://127.0.0.1/"));
        Map(app, session, host);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        var feature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var advertised = feature?.Addresses.FirstOrDefault() ?? app.Urls.FirstOrDefault();
        if (advertised is null || !Uri.TryCreate(advertised, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("El receptor no informó el puerto de escucha.");
        }

        host.BaseUri = new UriBuilder(uri) { Host = session.ExpectedHostName }.Uri;
        return host;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync().ConfigureAwait(false);

    private static void Map(WebApplication app, LanSession session, LanHost host)
    {
        app.MapPost("/v1/pair", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var body = await ReadJson<PairBody>(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body?.Code))
            {
                session.Database.Audit("emparejamiento", null, null, null, null, null, null, "desconocido");
                return Json(new { detail = "El código no es válido." }, StatusCodes.Status400BadRequest);
            }

            var result = session.Pairing.Redeem(body.Code, session.Fingerprint, body.DisplayName, body.PreviousDeviceId);
            return Json(
                result.Accepted
                    ? new { deviceId = result.DeviceId, credential = result.Credential, destinationId = result.DestinationId, destinationName = result.DestinationName, detail = result.Detail }
                    : new { detail = result.Detail },
                result.Accepted ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });

        app.MapPost("/v1/jobs", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!session.Pairing.TryAuthenticate(ReadDevice(request), ReadCredential(request), out var device) || device is null)
            {
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "La credencial del dispositivo no es válida." }, StatusCodes.Status401Unauthorized);
            }

            if (NamesAQueue(request))
            {
                session.Database.Audit("transferencia", device.Id, null, Header(request, "X-Npb-Source-Id"), null, null, null, "cola");
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "La petición no puede nombrar una cola de Windows. Hay que enviar el identificador del destino publicado." }, StatusCodes.Status400BadRequest);
            }

            var lane = host._lanes.GetOrAdd(device.Id, _ => new SemaphoreSlim(1, 1));
            if (!await lane.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                session.Database.Audit("transferencia", device.Id, null, Header(request, "X-Npb-Source-Id"), null, null, null, "en-curso");
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "Ya hay una transferencia en curso para este dispositivo." }, StatusCodes.Status429TooManyRequests);
            }

            try
            {
                var submission = new JobSubmission(
                    Header(request, "X-Npb-Source-Id") ?? "",
                    Header(request, "X-Npb-Destination-Id") ?? "",
                    request.ContentType ?? "",
                    null,
                    request.ContentLength);
                var result = await session.Intake.AcceptAsync(device, submission, request.Body, cancellationToken).ConfigureAwait(false);
                return Json(new { transfer = result.Transfer.ToString(), jobId = result.JobId, phase = result.Phase?.ToString(), detail = result.Detail }, Status(result));
            }
            finally
            {
                lane.Release();
            }
        });

        app.MapGet("/v1/jobs/{sourceId}", (string sourceId, HttpRequest request) =>
        {
            if (!session.Pairing.TryAuthenticate(ReadDevice(request), ReadCredential(request), out var device) || device is null)
            {
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "La credencial del dispositivo no es válida." }, StatusCodes.Status401Unauthorized);
            }

            if (!JobIntake.IsSafeSource(sourceId))
            {
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "El identificador de origen no es seguro." }, StatusCodes.Status400BadRequest);
            }

            var existing = session.Database.FindIdempotency(device.Id, sourceId);
            if (existing is null)
            {
                return Json(new { transfer = nameof(TransferState.Rechazada), detail = "No hay un trabajo aceptado con esa clave." }, StatusCodes.Status404NotFound);
            }

            return Json(new { transfer = nameof(TransferState.Aceptada), jobId = existing.JobId, phase = existing.Phase.ToString(), detail = "Fase guardada." }, StatusCodes.Status200OK);
        });

        app.MapGet("/v1/destinations", (HttpRequest request) =>
        {
            if (!session.Pairing.TryAuthenticate(ReadDevice(request), ReadCredential(request), out var device) || device is null)
            {
                return Json(new { detail = "La credencial del dispositivo no es válida." }, StatusCodes.Status401Unauthorized);
            }

            var destinations = session.Database.ListGranted(device.Id).Select(destination => new { id = destination.Id, displayName = destination.DisplayName });
            return Json(new { destinations }, StatusCodes.Status200OK);
        });

        app.MapPost("/v1/certificate/confirm", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!session.Pairing.TryAuthenticate(ReadDevice(request), ReadCredential(request), out var device) || device is null)
            {
                return Json(new { detail = "La credencial del dispositivo no es válida." }, StatusCodes.Status401Unauthorized);
            }

            var body = await ReadJson<ConfirmBody>(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body?.Fingerprint))
            {
                return Json(new { detail = "Falta la huella leída en el equipo anfitrión." }, StatusCodes.Status400BadRequest);
            }

            if (!HostCertificateTrust.IsCurrent(session.Presented.Certificate, DateTimeOffset.UtcNow))
            {
                session.Database.Audit("certificado", device.Id, null, null, null, null, null, "caducado");
                return Json(new { detail = "Un certificado caducado no autoriza su reemplazo." }, StatusCodes.Status400BadRequest);
            }

            try
            {
                session.ConfirmPending(body.Fingerprint);
            }
            catch (InvalidOperationException exception)
            {
                return Json(new { detail = exception.Message }, StatusCodes.Status400BadRequest);
            }

            var credential = session.Pairing.RotateCredential(device.Id, session.Fingerprint, session.ExpectedHostName);
            if (credential is null)
            {
                return Json(new { detail = "No se pudo rotar la credencial." }, StatusCodes.Status400BadRequest);
            }

            return Json(new { credential, detail = "El certificado quedó reemplazado." }, StatusCodes.Status200OK);
        });
    }

    private static bool NamesAQueue(HttpRequest request) =>
        !string.IsNullOrWhiteSpace(Header(request, "X-Npb-Queue-Name"))
        || !string.IsNullOrWhiteSpace(Header(request, "X-Npb-Queue"))
        || request.Query.Keys.Any(key => key.Equals("queue", StringComparison.OrdinalIgnoreCase) || key.Equals("queueName", StringComparison.OrdinalIgnoreCase));

    private static string ReadDevice(HttpRequest request) => ReadAuthorization(request).DeviceId;

    private static string ReadCredential(HttpRequest request) => ReadAuthorization(request).Credential;

    private static (string DeviceId, string Credential) ReadAuthorization(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var value))
        {
            return ("", "");
        }

        var parts = value.ToString().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[0].Equals("Npb", StringComparison.Ordinal) ? (parts[1], parts[2]) : ("", "");
    }

    private static string? Header(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

    private static async Task<T?> ReadJson<T>(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is 0)
        {
            return default;
        }

        return await request.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    private static IResult Json(object body, int status) => Results.Json(body, JsonOptions(), statusCode: status);

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int Status(IntakeResult result) => result.Transfer switch
    {
        TransferState.Aceptada => StatusCodes.Status200OK,
        TransferState.Conflicto => StatusCodes.Status409Conflict,
        _ => result.Detail.Contains("30 trabajos", StringComparison.Ordinal) ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest,
    };

    private sealed class PairBody
    {
        public string? Code { get; set; }

        public string? DisplayName { get; set; }

        public string? PreviousDeviceId { get; set; }

        public string? Fingerprint { get; set; }
    }

    private sealed class ConfirmBody
    {
        public string? Fingerprint { get; set; }
    }
}
