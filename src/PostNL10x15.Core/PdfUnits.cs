namespace PostNL10x15.Core;

public static class PdfUnits
{
    public const double PointsPerInch = 72.0;
    public const double MillimetersPerInch = 25.4;

    public static double MillimetersToPoints(double millimeters) =>
        millimeters * PointsPerInch / MillimetersPerInch;

    public static double PointsToMillimeters(double points) =>
        points * MillimetersPerInch / PointsPerInch;
}

