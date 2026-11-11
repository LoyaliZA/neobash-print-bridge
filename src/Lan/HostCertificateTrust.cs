using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Neobash.PrintBridge.Lan;

public static class HostCertificateTrust
{
    public const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    public static bool Allow(
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors,
        string expectedName,
        string connectedName,
        string pinnedFingerprint,
        DateTimeOffset now)
    {
        if (certificate is null)
        {
            return false;
        }

        if (!ChainErrorsAreOnlyUntrustedRoot(errors, chain?.ChainStatus.Select(status => status.Status).ToArray() ?? []))
        {
            return false;
        }

        if (!IsCurrent(certificate, now))
        {
            return false;
        }

        if (!HasServerUsage(certificate))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedName)
            || string.IsNullOrWhiteSpace(connectedName)
            || !string.Equals(expectedName.Trim(), connectedName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IdentityMatches(certificate, expectedName.Trim()))
        {
            return false;
        }

        return FingerprintEquals(certificate, pinnedFingerprint);
    }

    public static bool ChainErrorsAreOnlyUntrustedRoot(SslPolicyErrors errors, IReadOnlyList<X509ChainStatusFlags> statuses)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        var sawUntrustedRoot = false;
        foreach (var status in statuses)
        {
            if (status == X509ChainStatusFlags.NoError)
            {
                continue;
            }

            var other = status & ~X509ChainStatusFlags.UntrustedRoot;
            if (other != X509ChainStatusFlags.NoError || (status & X509ChainStatusFlags.UntrustedRoot) == 0)
            {
                return false;
            }

            sawUntrustedRoot = true;
        }

        return sawUntrustedRoot;
    }

    public static bool IsCurrent(X509Certificate2 certificate, DateTimeOffset now)
    {
        var instant = now.ToUniversalTime();
        return instant >= certificate.NotBefore.ToUniversalTime() && instant < certificate.NotAfter.ToUniversalTime();
    }

    public static bool HasServerUsage(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        if (usage is null)
        {
            return false;
        }

        var required = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;
        if ((usage.KeyUsages & required) != required)
        {
            return false;
        }

        var enhanced = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        if (enhanced is null || enhanced.EnhancedKeyUsages.Count != 1)
        {
            return false;
        }

        return enhanced.EnhancedKeyUsages[0].Value == ServerAuthenticationOid;
    }

    public static bool IdentityMatches(X509Certificate2 certificate, string expectedName)
    {
        foreach (var name in Names(certificate))
        {
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool FingerprintEquals(X509Certificate2 certificate, string pinnedFingerprint)
    {
        if (!TryDecodeFingerprint(pinnedFingerprint, out var pinned))
        {
            return false;
        }

        var actual = SHA256.HashData(certificate.RawData);
        return actual.Length == pinned.Length && CryptographicOperations.FixedTimeEquals(actual, pinned);
    }

    public static X509Certificate2 CreateSelfSigned(string hostName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        var usages = new OidCollection { new Oid(ServerAuthenticationOid) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
        var alternative = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(hostName, out var address))
        {
            alternative.AddIpAddress(address);
        }
        else
        {
            alternative.AddDnsName(hostName);
        }

        request.CertificateExtensions.Add(alternative.Build());
        using var issued = request.CreateSelfSigned(notBefore, notAfter);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = issued.Export(X509ContentType.Pfx, password);
        return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.Exportable);
    }

    public static IEnumerable<string> Names(X509Certificate2 certificate)
    {
        var common = certificate.GetNameInfo(X509NameType.SimpleName, false);
        if (!string.IsNullOrWhiteSpace(common))
        {
            yield return common;
        }

        foreach (var extension in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            foreach (var dns in extension.EnumerateDnsNames())
            {
                yield return dns;
            }

            foreach (var address in extension.EnumerateIPAddresses())
            {
                yield return address.ToString();
            }
        }
    }

    private static bool TryDecodeFingerprint(string text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var compact = text.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        try
        {
            bytes = Convert.FromHexString(compact);
        }
        catch (FormatException)
        {
            return false;
        }

        return bytes.Length == 32;
    }
}
