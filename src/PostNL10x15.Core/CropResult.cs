namespace PostNL10x15.Core;

public sealed record CropResult(
    string OutputPath,
    LabelBounds SourceBounds,
    double OutputWidthMillimeters,
    double OutputHeightMillimeters);

