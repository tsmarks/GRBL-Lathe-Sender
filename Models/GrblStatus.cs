namespace GRBL_Lathe_Control.Models;

public sealed record GrblStatus
{
    public string State { get; init; } = "Unknown";

    public double? MachineX { get; init; }

    public double? MachineZ { get; init; }

    public double? WorkX { get; init; }

    public double? WorkZ { get; init; }

    public bool XLimitPinHigh { get; init; }

    public bool ZLimitPinHigh { get; init; }

    public int? FeedOverridePercent { get; init; }

    public int? RapidOverridePercent { get; init; }

    public int? SpindleOverridePercent { get; init; }
}
