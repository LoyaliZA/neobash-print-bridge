using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Neobash.PrintBridge.Lan;

public interface ISecretStore
{
    void Put(string name, byte[] secret);

    byte[]? TryGet(string name);

    void Delete(string name);
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

    public void Put(string name, byte[] secret) => _secrets[name] = secret.ToArray();

    public byte[]? TryGet(string name) => _secrets.TryGetValue(name, out var secret) ? secret.ToArray() : null;

    public void Delete(string name) => _secrets.Remove(name);
}

public static class CurrentUserSecretStore
{
    public static ISecretStore Create(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Hace falta Credential Manager o DPAPI de la cuenta. Este equipo no ofrece ninguno.");
        }

        return CreateWindows(directory);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ISecretStore CreateWindows(string directory)
    {
        if (CredentialManagerSecretStore.IsSupported)
        {
            return new CredentialManagerSecretStore();
        }

        if (DpapiSecretStore.IsSupported)
        {
            return new DpapiSecretStore(directory);
        }

        throw new PlatformNotSupportedException("Hace falta Credential Manager o DPAPI de la cuenta. Este equipo no ofrece ninguno.");
    }
}

public sealed class CredentialManagerSecretStore : ISecretStore
{
    private const string Prefix = "Neobash.PrintBridge:";

    public static bool IsSupported
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                NativeCredentials.CredRead(Prefix + "probe", NativeCredentials.CredTypeGeneric, 0, out var pointer);
                if (pointer != IntPtr.Zero)
                {
                    NativeCredentials.CredFree(pointer);
                }

                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public void Put(string name, byte[] secret)
    {
        var blob = Marshal.AllocHGlobal(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var credential = new NativeCredentials.Credential
            {
                Type = NativeCredentials.CredTypeGeneric,
                TargetName = Prefix + name,
                CredentialBlobSize = secret.Length,
                CredentialBlob = blob,
                Persist = NativeCredentials.CredPersistLocalMachine,
                UserName = "Neobash.PrintBridge",
            };
            if (!NativeCredentials.CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("Credential Manager no guardó el secreto de la cuenta.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public byte[]? TryGet(string name)
    {
        if (!NativeCredentials.CredRead(Prefix + name, NativeCredentials.CredTypeGeneric, 0, out var pointer) || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredentials.Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return null;
            }

            var secret = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
            return secret;
        }
        finally
        {
            NativeCredentials.CredFree(pointer);
        }
    }

    public void Delete(string name) =>
        NativeCredentials.CredDelete(Prefix + name, NativeCredentials.CredTypeGeneric, 0);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore(string directory)
    {
        _directory = Path.Combine(Path.GetFullPath(directory), "secrets");
        Directory.CreateDirectory(_directory);
    }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public void Put(string name, byte[] secret)
    {
        var protectedBytes = ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(name), protectedBytes);
    }

    public byte[]? TryGet(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string name)
    {
        var file = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))) + ".bin";
        var path = Path.GetFullPath(Path.Combine(_directory, file));
        var root = _directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El secreto quedaría fuera del almacén de la cuenta.");
        }

        return path;
    }
}

internal static class NativeCredentials
{
    internal const int CredTypeGeneric = 1;

    internal const int CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Credential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    internal static extern void CredFree(IntPtr credential);
}
