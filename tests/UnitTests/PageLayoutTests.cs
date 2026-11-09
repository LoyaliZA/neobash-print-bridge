using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Tests;

public sealed class PageLayoutTests
{
    private static readonly PrintableBox Receipt = new(0.25, 0.25, 2.90, 10);

    [Fact]
    public void Percentage_uses_the_pdf_physical_size_and_not_the_paper()
    {
        var result = PageLayoutCalculator.Place(8, 10, new PrintableBox(0.25, 0.25, 7.6, 10.5), 203, 203, ScaleMode.Percentage, 90);

        Assert.True(result.Accepted);
        Assert.Equal(7.2, result.Placement.Value.WidthInches, 3);
        Assert.Equal(9.0, result.Placement.Value.HeightInches, 3);
        Assert.Equal(0.25, result.Placement.Value.XInches, 3);
        Assert.Equal(0.25, result.Placement.Value.YInches, 3);
        Assert.NotEqual(7.6 * 0.90, result.Placement.Value.WidthInches, 3);
        Assert.Equal((int)Math.Round(7.2 * 203, MidpointRounding.AwayFromZero), result.Placement.Value.PixelWidth);
        Assert.Equal((int)Math.Round(9.0 * 203, MidpointRounding.AwayFromZero), result.Placement.Value.PixelHeight);
    }

    [Fact]
    public void Actual_size_keeps_the_pdf_dimensions_when_they_fit()
    {
        var result = PageLayoutCalculator.Place(2, 4, Receipt, 203, 180, ScaleMode.ActualSize, null);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Placement.Value.WidthInches, 3);
        Assert.Equal(4, result.Placement.Value.HeightInches, 3);
        Assert.Equal(0.25, result.Placement.Value.XInches, 3);
    }

    [Fact]
    public void Actual_size_rejects_a_page_that_does_not_fit_without_shrinking_it()
    {
        var result = PageLayoutCalculator.Place(8, 10, Receipt, 203, 203, ScaleMode.ActualSize, null);

        Assert.False(result.Accepted);
        Assert.Null(result.Placement);
        Assert.Contains("segundo ajuste", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.RequiresClippingConfirmation);
        Assert.Contains("203.20", result.Error);
        Assert.Contains("73.66", result.Error);
        Assert.Contains("ancho", result.Error);
    }

    [Fact]
    public void Confirmed_clipping_keeps_requested_scale_and_resolution()
    {
        var result = PageLayoutCalculator.Place(8, 12, Receipt, 203, 180, ScaleMode.Percentage, 91, true);

        Assert.True(result.Accepted);
        Assert.False(result.RequiresClippingConfirmation);
        Assert.Equal(7.28, result.Placement!.Value.WidthInches, 3);
        Assert.Equal(10.92, result.Placement.Value.HeightInches, 3);
        Assert.Equal((int)Math.Round(7.28 * 203), result.Placement.Value.PixelWidth);
    }

    [Fact]
    public void Height_overflow_reports_height_and_scaled_dimensions()
    {
        var result = PageLayoutCalculator.Place(2, 12, Receipt, 203, 203, ScaleMode.ActualSize, null);

        Assert.False(result.Accepted);
        Assert.True(result.RequiresClippingConfirmation);
        Assert.StartsWith("Se excede el alto.", result.Error);
        Assert.Contains("50.80 × 304.80", result.Error);
    }

    [Fact]
    public void Clipping_consent_does_not_bypass_raster_memory_limit()
    {
        var result = PageLayoutCalculator.Place(40, 40, Receipt, 1200, 1200, ScaleMode.ActualSize, null, true);

        Assert.False(result.Accepted);
        Assert.False(result.RequiresClippingConfirmation);
        Assert.Contains("memoria", result.Error);
    }

    [Fact]
    public void Fit_uses_only_the_printable_area()
    {
        var result = PageLayoutCalculator.Place(8, 10, new PrintableBox(0.2, 0.3, 4, 6), 300, 300, ScaleMode.FitPrintableArea, null);

        Assert.True(result.Accepted);
        Assert.Equal(4, result.Placement.Value.WidthInches, 3);
        Assert.Equal(5, result.Placement.Value.HeightInches, 3);
        Assert.Equal(0.2, result.Placement.Value.XInches, 3);
        Assert.Equal(0.3, result.Placement.Value.YInches, 3);
    }

    [Fact]
    public void Receipt_fit_starts_at_the_top_even_with_a_three_metre_driver_page()
    {
        var area = new PrintableBox(0, 0.02, 72.07 / 25.4, 3002.96 / 25.4);
        var result = PageLayoutCalculator.Place(80 / 25.4, 129.39 / 25.4, area, 203, 203, ScaleMode.FitPrintableArea, null);

        Assert.True(result.Accepted);
        Assert.Equal(area.YInches, result.Placement!.Value.YInches);
        Assert.Equal(72.07, result.Placement.Value.WidthInches * 25.4, 2);
        Assert.InRange(result.Placement.Value.HeightInches * 25.4, 116, 117);
    }

    [Fact]
    public void Fit_does_not_accept_a_second_percentage()
    {
        var result = PageLayoutCalculator.Place(8, 10, Receipt, 203, 203, ScaleMode.FitPrintableArea, 90);

        Assert.False(result.Accepted);
        Assert.Contains("no se combina", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raster_size_follows_the_destination_resolution()
    {
        var slow = PageLayoutCalculator.Place(2, 2, Receipt, 203, 203, ScaleMode.ActualSize, null);
        var fast = PageLayoutCalculator.Place(2, 2, Receipt, 300, 300, ScaleMode.ActualSize, null);

        Assert.True(slow.Accepted);
        Assert.True(fast.Accepted);
        Assert.NotEqual(slow.Placement!.Value.PixelWidth, fast.Placement!.Value.PixelWidth);
        Assert.Equal(203, slow.Placement.Value.DpiX);
        Assert.Equal(300, fast.Placement.Value.DpiX);
    }

    [Fact]
    public void Missing_resolution_is_rejected()
    {
        var result = PageLayoutCalculator.Place(2, 2, Receipt, 0, 203, ScaleMode.ActualSize, null);

        Assert.False(result.Accepted);
        Assert.Contains("DPI genérico", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oversized_raster_is_rejected_before_allocation()
    {
        var result = PageLayoutCalculator.Place(40, 40, new PrintableBox(0, 0, 40, 40), 1200, 1200, ScaleMode.ActualSize, null);

        Assert.False(result.Accepted);
        Assert.Contains("memoria", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dips_convert_without_changing_the_physical_inch()
    {
        var (width, height) = PageLayoutCalculator.PhysicalSizeFromDips(96, 192);

        Assert.Equal(1, width, 5);
        Assert.Equal(2, height, 5);
    }

    [Theory]
    [InlineData(-25f, -25f, 0.25, 0.25, 0f, 0f)]
    [InlineData(0f, 0f, 0.25, 0.25, 25f, 25f)]
    public void Graphics_origin_keeps_the_physical_margin(float pageX, float pageY, double placeX, double placeY, float expectedX, float expectedY)
    {
        var point = PrintCoordinates.FromPhysicalInches(placeX, placeY, pageX, pageY);

        Assert.Equal(expectedX, point.X);
        Assert.Equal(expectedY, point.Y);
    }
}
