using System.Xml.Linq;

namespace Neobash.PrintBridge.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void Msix_identity_is_stable_and_versioned_for_replacement_updates()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "src", "Windows", "VirtualPrinter", "Package.appxmanifest");
        var propsPath = Path.Combine(root, "src", "Windows", "Directory.Build.props");
        var manifest = XDocument.Load(manifestPath);
        var props = XDocument.Load(propsPath);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace printSupport2 = "http://schemas.microsoft.com/appx/manifest/printsupport/windows10/2";

        var identity = manifest.Root!.Element(ns + "Identity")!;
        var packageName = Property(props, "NpbPackageName");
        var publisher = Property(props, "NpbPackagePublisher");
        var version = Property(props, "NpbPackageVersion");
        var printer = manifest.Descendants(printSupport2 + "PrintSupportVirtualPrinter").Single();

        Assert.Equal("Neobash.PrintBridge.VirtualPrinter", packageName);
        Assert.Equal("CN=Neobash Print Bridge POC", publisher);
        Assert.Equal(packageName, (string?)identity.Attribute("Name"));
        Assert.Equal(publisher, (string?)identity.Attribute("Publisher"));
        Assert.Equal(version, (string?)identity.Attribute("Version"));
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", version);
        Assert.Equal("ms-resource:PrintSupportVirtualPrinterDisplayName", (string?)printer.Attribute("DisplayName"));
        Assert.Equal("1.0.1.0", version);
        Assert.Equal("application/oxps", (string?)printer.Attribute("PreferredInputFormat"));
        Assert.Null(printer.Attribute("OutputFileTypes"));
        Assert.Contains("runFullTrust", File.ReadAllText(manifestPath), StringComparison.Ordinal);
        Assert.DoesNotContain("internetClient", File.ReadAllText(manifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Core_has_no_WinUI_or_fixed_package_path()
    {
        var project = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Core", "Neobash.PrintBridge.Core.csproj"));
        Assert.DoesNotContain("UseWinUI", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", project, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalState", project, StringComparison.Ordinal);
    }

    private static string Property(XDocument props, string name) =>
        props.Descendants(name).Single().Value;

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
}
