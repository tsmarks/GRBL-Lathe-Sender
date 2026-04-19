namespace GRBL_Lathe_Control.Models;

public sealed record GrblStatus
{
    public string State { get; init; } = "Unknown";

    public double? MachineX { get; init; }

    public double? MachineY { get; init; }

    public double? MachineZ { get; init; }

    public double? MachineA { get; init; }

    public double? MachineB { get; init; }

    public double? WorkX { get; init; }

    public double? WorkY { get; init; }

    public double? WorkZ { get; init; }

    public double? WorkA { get; init; }

    public double? WorkB { get; init; }

    public bool XLimitPinHigh { get; init; }

    public bool YLimitPinHigh { get; init; }

    public bool ZLimitPinHigh { get; init; }

    public bool ProbePinHigh { get; init; }

    public int? FeedOverridePercent { get; init; }

    public int? RapidOverridePercent { get; init; }

    public int? SpindleOverridePercent { get; init; }
}
