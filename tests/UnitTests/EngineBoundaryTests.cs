namespace Neobash.PrintBridge.Tests;

public sealed class EngineBoundaryTests
{
    [Fact]
    public void Shared_engine_has_no_WinUI_and_keeps_the_desktop_native_packages()
    {
        var root = FindRepositoryRoot();
        var library = File.ReadAllText(Path.Combine(root, "src", "Windows", "PrintEngine", "Neobash.PrintBridge.PrintEngine.csproj"));
        var desktop = File.ReadAllText(Path.Combine(root, "src", "Windows", "Desktop", "Neobash.PrintBridge.Desktop.csproj"));
        var core = File.ReadAllText(Path.Combine(root, "src", "Core", "Neobash.PrintBridge.Core.csproj"));
        var windowsTests = File.ReadAllText(Path.Combine(root, "tests", "WindowsPrintTests", "Neobash.PrintBridge.WindowsPrintTests.csproj"));

        Assert.DoesNotContain("UseWinUI", library, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", library, StringComparison.Ordinal);
        Assert.Contains("System.Drawing.Common", library, StringComparison.Ordinal);
        Assert.Contains("10.0.0", library, StringComparison.Ordinal);
        Assert.Contains("bblanchon.PDFium.Win32", library, StringComparison.Ordinal);
        Assert.Contains("157.0.8100", library, StringComparison.Ordinal);
        Assert.Contains("PrintEngine", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWinUI", core, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", core, StringComparison.Ordinal);
        Assert.Contains("PrintEngine", windowsTests, StringComparison.Ordinal);
        Assert.DoesNotContain("PdfiumPrintSession.cs", windowsTests, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWinUI", windowsTests, StringComparison.Ordinal);
    }

    [Fact]
    public void Moved_engine_keeps_the_print_sta_thread_name_and_emf_mode()
    {
        var root = FindRepositoryRoot();
        var adapter = File.ReadAllText(Path.Combine(root, "src", "Windows", "PrintEngine", "PdfSpoolerAdapter.cs"));
        var session = File.ReadAllText(Path.Combine(root, "src", "Windows", "PrintEngine", "PdfiumPrintSession.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "src", "Core", "Dispatch", "PrintDispatcher.cs"));
        var layout = File.ReadAllText(Path.Combine(root, "src", "Core", "Dispatch", "PageLayoutCalculator.cs"));

        Assert.Contains("Name = \"NPB print STA\"", adapter, StringComparison.Ordinal);
        Assert.Contains("FPDF_PRINTMODE_EMF", session, StringComparison.Ordinal);
        Assert.Contains("class PrintDispatcher", dispatcher, StringComparison.Ordinal);
        Assert.Contains("class PageLayoutCalculator", layout, StringComparison.Ordinal);
        var engineFiles = Directory.GetFiles(Path.Combine(root, "src", "Windows", "PrintEngine")).Select(Path.GetFileName).ToArray();
        Assert.DoesNotContain("PrintDispatcher.cs", engineFiles);
        Assert.DoesNotContain("PageLayoutCalculator.cs", engineFiles);
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
}
