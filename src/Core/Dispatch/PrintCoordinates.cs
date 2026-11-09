namespace Neobash.PrintBridge.Core.Dispatch;

public readonly record struct GraphicsPoint(float X, float Y);

public static class PrintCoordinates
{
    public static GraphicsPoint FromPhysicalInches(double xInches, double yInches, float pageBoundsX, float pageBoundsY) =>
        new(pageBoundsX + (float)(xInches * 100.0), pageBoundsY + (float)(yInches * 100.0));
}
