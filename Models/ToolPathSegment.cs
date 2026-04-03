namespace GRBL_Lathe_Control.Models;

public sealed record ToolPathSegment(
    double StartZ,
    double StartX,
    double EndZ,
    double EndX,
    bool IsRapid);
