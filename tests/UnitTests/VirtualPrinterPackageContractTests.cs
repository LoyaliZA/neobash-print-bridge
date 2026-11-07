using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class VirtualPrinterPackageContractTests
{
    private const string PrinterName = "NPB - Prueba Virtual";
    private const string ResourceName = "PrintSupportVirtualPrinterDisplayName";
    private const string ResourceReference = "ms-resource:" + ResourceName;
    private const string VirtualPrinterClass = "Neobash.PrintBridge.VirtualPrinter.Tasks.VirtualPrinterBackgroundTask";
    private const string ExtensionClass = "Neobash.PrintBridge.VirtualPrinter.Tasks.PrintSupportExtensionBackgroundTask";
    private const string HostPath = "WinRT.Host.dll";
    private const string PdcPath = @"Config\NpbPruebaVirtual.pdc.xml";

    [Fact]
    public void Source_display_name_references_the_localized_resource()
    {
        var manifest = XDocument.Load(SourceManifestPath());
        XNamespace printSupport2 = "http://schemas.microsoft.com/appx/manifest/printsupport/windows10/2";
        var printer = manifest.Descendants(printSupport2 + "PrintSupportVirtualPrinter").Single();
        var reference = (string?)printer.Attribute("DisplayName");

        Assert.Equal(ResourceReference, reference);
        Assert.Equal(PrinterName, ReadReswValue(Path.Combine(VirtualPrinterDirectory(), "Strings", "es-ES", "Resources.resw")));
        Assert.Equal(PrinterName, ReadReswValue(Path.Combine(VirtualPrinterDirectory(), "Strings", "en-US", "Resources.resw")));
    }

    [Fact]
    public void Source_declares_print_support_extension_and_its_activatable_class()
    {
        var manifest = XDocument.Load(SourceManifestPath());
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace printSupport = "http://schemas.microsoft.com/appx/manifest/printsupport/windows10";
        var extension = manifest.Descendants(printSupport + "Extension")
            .Single(element => (string?)element.Attribute("Category") == "windows.printSupportExtension");
        var classIds = manifest.Descendants(foundation + "ActivatableClass")
            .Select(element => (string?)element.Attribute("ActivatableClassId"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ExtensionClass, (string?)extension.Attribute("EntryPoint"));
        Assert.Contains(ExtensionClass, classIds);
        Assert.Contains(VirtualPrinterClass, classIds);
        Assert.Contains("printsupport", manifest.Root!.Attribute("IgnorableNamespaces")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdc_options_match_the_print_ticket_contract()
    {
        var document = XDocument.Load(Path.Combine(VirtualPrinterDirectory(), "Config", "NpbPruebaVirtual.pdc.xml"));
        var actual = document.Root!.Elements()
            .Where(element => (string?)element.Attribute(element.Name.Namespace + "psftype") == "Feature" || element.Name.LocalName is "PageMediaSize" or "PageOrientation" or "PageOutputColor")
            .ToDictionary(
                element => element.Name.LocalName,
                element => element.Elements().Select(option => option.Name.LocalName).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        Assert.Equal(VirtualPrinterPrintTicket.DeclaredFeatures.Keys.ToHashSet(StringComparer.Ordinal), actual.Keys.ToHashSet(StringComparer.Ordinal));
        foreach (var feature in VirtualPrinterPrintTicket.DeclaredFeatures)
        {
            Assert.Equal(feature.Value.ToHashSet(StringComparer.Ordinal), actual[feature.Key]);
        }
    }

    [Fact]
    public void Packaged_manifest_resolves_the_printer_resource_and_registers_the_extension()
    {
        var package = OpenPackage();
        var manifest = package.Manifest;
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace printSupport = "http://schemas.microsoft.com/appx/manifest/printsupport/windows10";
        XNamespace printSupport2 = "http://schemas.microsoft.com/appx/manifest/printsupport/windows10/2";

        var identity = manifest.Root!.Element(foundation + "Identity")!;
        var printer = manifest.Descendants(printSupport2 + "PrintSupportVirtualPrinter").Single();
        var extension = manifest.Descendants(printSupport + "Extension")
            .Single(element => (string?)element.Attribute("Category") == "windows.printSupportExtension");
        var host = manifest.Descendants(foundation + "InProcessServer").Single();
        var classIds = host.Elements(foundation + "ActivatableClass")
            .Select(element => (string?)element.Attribute("ActivatableClassId"))
            .ToHashSet(StringComparer.Ordinal);
        var pdc = ((string?)printer.Attribute("PdcFile") ?? "").Replace('\\', '/');

        Assert.Equal("1.0.1.0", (string?)identity.Attribute("Version"));
        Assert.Equal("Neobash.PrintBridge.VirtualPrinter", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=Neobash Print Bridge POC", (string?)identity.Attribute("Publisher"));
        Assert.Equal(ResourceReference, (string?)printer.Attribute("DisplayName"));
        Assert.NotEqual(File.ReadAllText(SourceManifestPath()), File.ReadAllText(package.ManifestPath));
        Assert.Equal(ExtensionClass, (string?)extension.Attribute("EntryPoint"));
        Assert.Equal(HostPath, (string?)host.Element(foundation + "Path"));
        Assert.Contains(ExtensionClass, classIds);
        Assert.Contains(VirtualPrinterClass, classIds);
        Assert.Contains("resources.pri", package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(pdc, package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(HostPath, package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(HostDirectory(HostPath) + "Neobash.PrintBridge.VirtualPrinter.Tasks.dll", package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(HostDirectory(HostPath) + "Neobash.PrintBridge.VirtualPrinter.Tasks.winmd", package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(HostDirectory(HostPath) + "WinRT.Host.runtimeconfig.json", package.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(package.Entries, entry => entry.EndsWith("coreclr.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(package.Entries, entry => entry.EndsWith("hostfxr.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Microsoft.NETCore.App", package.HostRuntimeConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("\"includedFrameworks\"", package.HostRuntimeConfig, StringComparison.Ordinal);
        Assert.Contains(ExtensionClass, package.WinRtClasses);
        Assert.Contains(VirtualPrinterClass, package.WinRtClasses);
        Assert.Equal(PrinterName, package.ResolvedPrinterName);
    }

    private static string HostDirectory(string hostPath)
    {
        var directory = Path.GetDirectoryName(hostPath.Replace('\\', '/'))?.Replace('\\', '/');
        return string.IsNullOrEmpty(directory) ? "" : directory + "/";
    }

    private static string ReadReswValue(string path)
    {
        var document = XDocument.Load(path);
        return document.Descendants("data")
            .Single(element => (string?)element.Attribute("name") == ResourceName)
            .Element("value")!.Value;
    }

    private static string SourceManifestPath() => Path.Combine(VirtualPrinterDirectory(), "Package.appxmanifest");

    private static string VirtualPrinterDirectory() =>
        Path.Combine(FindRepositoryRoot(), "src", "Windows", "VirtualPrinter");

    private static PackagedPrinter OpenPackage()
    {
        var root = FindRepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "src", "Windows", "Directory.Build.props"));
        var version = props.Descendants("NpbPackageVersion").Single().Value;
        var packagePath = Path.Combine(
            root,
            "src",
            "Windows",
            "VirtualPrinter",
            "AppPackages",
            $"Neobash.PrintBridge.VirtualPrinter_{version}_x64_Test",
            $"Neobash.PrintBridge.VirtualPrinter_{version}_x64.msix");
        Assert.True(File.Exists(packagePath), $"No está el MSIX empaquetado {packagePath}.");

        using var archive = ZipFile.OpenRead(packagePath);
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(Path.GetTempPath(), "npb-appxmanifest-" + version + ".xml");
        var priPath = Path.Combine(Path.GetTempPath(), "npb-resources-" + version + ".pri");
        var winmdPath = Path.Combine(Path.GetTempPath(), "npb-tasks-" + version + ".winmd");
        var runtimeConfigPath = Path.Combine(Path.GetTempPath(), "npb-winrt-host-" + version + ".runtimeconfig.json");
        Extract(archive, "AppxManifest.xml", manifestPath);
        Extract(archive, "resources.pri", priPath);
        Extract(archive, "Neobash.PrintBridge.VirtualPrinter.Tasks.winmd", winmdPath);
        Extract(archive, "WinRT.Host.runtimeconfig.json", runtimeConfigPath);

        return new PackagedPrinter(
            manifestPath,
            XDocument.Load(manifestPath),
            names,
            File.ReadAllText(runtimeConfigPath),
            ReadWinRtClasses(winmdPath),
            ResolvePrinterName(priPath));
    }

    private static void Extract(ZipArchive archive, string entryName, string destination)
    {
        var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException($"El MSIX no contiene {entryName}.");
        entry.ExtractToFile(destination, overwrite: true);
    }

    private static IReadOnlyList<string> ReadWinRtClasses(string winmdPath)
    {
        using var stream = File.OpenRead(winmdPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!type.Attributes.HasFlag(System.Reflection.TypeAttributes.Public) || type.Attributes.HasFlag(System.Reflection.TypeAttributes.Abstract))
            {
                continue;
            }

            var typeNamespace = reader.GetString(type.Namespace);
            var typeName = reader.GetString(type.Name);
            if (string.IsNullOrEmpty(typeNamespace) || typeName.StartsWith('<') || typeName == "<Module>")
            {
                continue;
            }

            names.Add(typeNamespace + "." + typeName);
        }

        return names;
    }

    private static string ResolvePrinterName(string priPath)
    {
        var makePri = FindMakePri();
        var dumpPath = Path.Combine(Path.GetTempPath(), "npb-resources-dump-" + Guid.NewGuid().ToString("N") + ".xml");
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = makePri,
            Arguments = $"dump /if \"{priPath}\" /of \"{dumpPath}\" /dt Detailed",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar makepri.");
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("makepri no terminó al volcar resources.pri.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"makepri terminó con el código {process.ExitCode}.");
        }

        var dump = XDocument.Load(dumpPath);
        var defaultLanguage = dump.Descendants("Qualifier")
            .Where(element => (string?)element.Attribute("name") == "Language")
            .OrderByDescending(element => double.Parse((string?)element.Attribute("scoreAsDefault") ?? "0", System.Globalization.CultureInfo.InvariantCulture))
            .Select(element => (string?)element.Attribute("value"))
            .First();
        var resource = dump.Descendants("NamedResource")
            .Single(element => (string?)element.Attribute("name") == ResourceName);
        var candidates = resource.Elements("Candidate")
            .Where(element => (string?)element.Attribute("type") == "String")
            .Select(element => new
            {
                Language = (string?)element.Element("QualifierSet")?.Elements("Qualifier").FirstOrDefault(qualifier => (string?)qualifier.Attribute("name") == "Language")?.Attribute("value"),
                Value = element.Element("Value")?.Value
            })
            .ToArray();

        var spanish = candidates.Single(candidate => candidate.Language == "ES-ES");
        var fallback = candidates.Single(candidate => candidate.Language == defaultLanguage);
        Assert.Equal(PrinterName, spanish.Value);
        Assert.Equal(PrinterName, fallback.Value);
        return fallback.Value!;
    }

    private static string FindMakePri()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "bin")
        };
        var match = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "makepri.exe", SearchOption.AllDirectories))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrEmpty(match), "No se encontró makepri.exe del Windows SDK.");
        return match!;
    }

    private static string FindRepositoryRoot()
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

    private sealed record PackagedPrinter(
        string ManifestPath,
        XDocument Manifest,
        IReadOnlySet<string> Entries,
        string HostRuntimeConfig,
        IReadOnlyList<string> WinRtClasses,
        string ResolvedPrinterName);
}
