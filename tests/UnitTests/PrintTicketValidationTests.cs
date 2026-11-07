using Neobash.PrintBridge.Core.Capture;

namespace Neobash.PrintBridge.Tests;

public sealed class PrintTicketValidationTests
{
    [Fact]
    public void Declared_page_options_are_valid()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1">
              <psf:Feature name="psk:PageMediaSize">
                <psf:Option name="psk:NorthAmericaLetter" />
              </psf:Feature>
              <psf:Feature name="psk:PageOrientation">
                <psf:Option name="psk:Landscape" />
              </psf:Feature>
              <psf:Feature name="psk:PageOutputColor">
                <psf:Option name="psk:Monochrome" />
              </psf:Feature>
            </psf:PrintTicket>
            """;

        Assert.True(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Fact]
    public void Omitted_features_keep_the_pdc_defaults()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" version="1" />
            """;

        Assert.True(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Fact]
    public void Features_outside_the_pdc_do_not_invalidate_the_ticket()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1">
              <psf:Feature name="psk:JobCopiesAllDocuments">
                <psf:Option name="psk:Copies" />
              </psf:Feature>
              <psf:Feature name="psk:PageMediaSize">
                <psf:Option name="psk:ISOA4" />
              </psf:Feature>
            </psf:PrintTicket>
            """;

        Assert.True(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<not-xml")]
    [InlineData("<psf:PrintCapabilities xmlns:psf=\"http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework\" />")]
    public void Missing_or_foreign_documents_are_invalid(string? ticket)
    {
        Assert.False(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Fact]
    public void Option_outside_the_pdc_is_invalid()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1">
              <psf:Feature name="psk:PageMediaSize">
                <psf:Option name="psk:NorthAmericaTabloid" />
              </psf:Feature>
            </psf:PrintTicket>
            """;

        Assert.False(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Fact]
    public void Declared_feature_without_one_option_is_invalid()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1">
              <psf:Feature name="psk:PageOrientation">
                <psf:Option name="psk:Portrait" />
                <psf:Option name="psk:Landscape" />
              </psf:Feature>
            </psf:PrintTicket>
            """;

        Assert.False(VirtualPrinterPrintTicket.IsValid(ticket));
    }

    [Fact]
    public void Repeated_declared_feature_is_invalid()
    {
        const string ticket = """
            <psf:PrintTicket xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework" xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords" version="1">
              <psf:Feature name="psk:PageOutputColor">
                <psf:Option name="psk:Color" />
              </psf:Feature>
              <psf:Feature name="psk:PageOutputColor">
                <psf:Option name="psk:Monochrome" />
              </psf:Feature>
            </psf:PrintTicket>
            """;

        Assert.False(VirtualPrinterPrintTicket.IsValid(ticket));
    }
}
