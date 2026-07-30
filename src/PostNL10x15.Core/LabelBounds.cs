namespace PostNL10x15.Core;

/// <summary>
/// Coordinates in PDF points, measured from the top-left of the first page.
/// </summary>
public sealed record LabelBounds(
    double X,
    double Y,
    double Width,
    double Height,
    double PageWidth,
    double PageHeight)
{
    public bool IsLandscape => Width >= Height;

    public double WidthMillimeters => PdfUnits.PointsToMillimeters(Width);

    public double HeightMillimeters => PdfUnits.PointsToMillimeters(Height);
}

