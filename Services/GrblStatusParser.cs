using System;
using System.Globalization;
using GRBL_Lathe_Control.Models;

namespace GRBL_Lathe_Control.Services;

public static class GrblStatusParser
{
    public static bool TryParse(string line, out GrblStatus status)
    {
        status = new GrblStatus();

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith('<') || !trimmed.EndsWith('>'))
        {
            return false;
        }

        var fields = trimmed[1..^1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length == 0)
        {
            return false;
        }

        double[]? machineCoordinates = null;
        double[]? workCoordinates = null;
        double[]? workOffsets = null;
        var xLimitPinHigh = false;
        var yLimitPinHigh = false;
        var zLimitPinHigh = false;
        var probePinHigh = false;
        int? feedOverridePercent = null;
        int? rapidOverridePercent = null;
        int? spindleOverridePercent = null;

        for (var index = 1; index < fields.Length; index++)
        {
            var field = fields[index];

            if (field.StartsWith("MPos:", StringComparison.OrdinalIgnoreCase))
            {
                machineCoordinates = ParseCoordinateList(field[5..]);
            }
            else if (field.StartsWith("WPos:", StringComparison.OrdinalIgnoreCase))
            {
                workCoordinates = ParseCoordinateList(field[5..]);
            }
            else if (field.StartsWith("WCO:", StringComparison.OrdinalIgnoreCase))
            {
                workOffsets = ParseCoordinateList(field[4..]);
            }
            else if (field.StartsWith("Pn:", StringComparison.OrdinalIgnoreCase))
            {
                var pinState = field[3..];
                xLimitPinHigh = pinState.Contains('X', StringComparison.OrdinalIgnoreCase);
                yLimitPinHigh = pinState.Contains('Y', StringComparison.OrdinalIgnoreCase);
                zLimitPinHigh = pinState.Contains('Z', StringComparison.OrdinalIgnoreCase);
                probePinHigh = pinState.Contains('P', StringComparison.OrdinalIgnoreCase);
            }
            else if (field.StartsWith("Ov:", StringComparison.OrdinalIgnoreCase))
            {
                var overrideValues = ParseIntegerList(field[3..]);
                if (overrideValues.Length >= 1)
                {
                    feedOverridePercent = overrideValues[0];
                }

                if (overrideValues.Length >= 2)
                {
                    rapidOverridePercent = overrideValues[1];
                }

                if (overrideValues.Length >= 3)
                {
                    spindleOverridePercent = overrideValues[2];
                }
            }
        }

        var machineX = machineCoordinates is not null
            ? ResolveAxis(machineCoordinates, 0)
            : Add(ResolveAxis(workCoordinates, 0), ResolveAxis(workOffsets, 0));

        var machineY = machineCoordinates is not null
            ? ResolveAxis(machineCoordinates, 1)
            : Add(ResolveAxis(workCoordinates, 1), ResolveAxis(workOffsets, 1));

        var machineZ = machineCoordinates is not null
            ? ResolveAxis(machineCoordinates, ResolveZAxisIndex(machineCoordinates))
            : Add(
                ResolveAxis(workCoordinates, ResolveZAxisIndex(workCoordinates)),
                ResolveAxis(workOffsets, ResolveZAxisIndex(workOffsets)));

        var machineA = machineCoordinates is not null
            ? ResolveAxis(machineCoordinates, 3)
            : Add(ResolveAxis(workCoordinates, 3), ResolveAxis(workOffsets, 3));

        var machineB = machineCoordinates is not null
            ? ResolveAxis(machineCoordinates, 4)
            : Add(ResolveAxis(workCoordinates, 4), ResolveAxis(workOffsets, 4));

        var workX = workCoordinates is not null
            ? ResolveAxis(workCoordinates, 0)
            : Subtract(ResolveAxis(machineCoordinates, 0), ResolveAxis(workOffsets, 0));

        var workY = workCoordinates is not null
            ? ResolveAxis(workCoordinates, 1)
            : Subtract(ResolveAxis(machineCoordinates, 1), ResolveAxis(workOffsets, 1));

        var workZ = workCoordinates is not null
            ? ResolveAxis(workCoordinates, ResolveZAxisIndex(workCoordinates))
            : Subtract(
                ResolveAxis(machineCoordinates, ResolveZAxisIndex(machineCoordinates)),
                ResolveAxis(workOffsets, ResolveZAxisIndex(workOffsets)));

        var workA = workCoordinates is not null
            ? ResolveAxis(workCoordinates, 3)
            : Subtract(ResolveAxis(machineCoordinates, 3), ResolveAxis(workOffsets, 3));

        var workB = workCoordinates is not null
            ? ResolveAxis(workCoordinates, 4)
            : Subtract(ResolveAxis(machineCoordinates, 4), ResolveAxis(workOffsets, 4));

        if (machineX is null && machineY is null && machineZ is null && workX is null && workY is null && workZ is null)
        {
            return false;
        }

        status = new GrblStatus
        {
            State = fields[0],
            MachineX = machineX,
            MachineY = machineY,
            MachineZ = machineZ,
            MachineA = machineA,
            MachineB = machineB,
            WorkX = workX,
            WorkY = workY,
            WorkZ = workZ,
            WorkA = workA,
            WorkB = workB,
            XLimitPinHigh = xLimitPinHigh,
            YLimitPinHigh = yLimitPinHigh,
            ZLimitPinHigh = zLimitPinHigh,
            ProbePinHigh = probePinHigh,
            FeedOverridePercent = feedOverridePercent,
            RapidOverridePercent = rapidOverridePercent,
            SpindleOverridePercent = spindleOverridePercent
        };

        return true;
    }

    private static int ResolveZAxisIndex(double[]? coordinates)
    {
        if (coordinates is null)
        {
            return 0;
        }

        return coordinates.Length >= 3 ? 2 : Math.Min(1, coordinates.Length - 1);
    }

    private static double? ResolveAxis(double[]? coordinates, int index)
    {
        if (coordinates is null || index < 0 || index >= coordinates.Length)
        {
            return null;
        }

        return coordinates[index];
    }

    private static double? Add(double? left, double? right)
    {
        return left.HasValue && right.HasValue
            ? left.Value + right.Value
            : null;
    }

    private static double? Subtract(double? left, double? right)
    {
        return left.HasValue && right.HasValue
            ? left.Value - right.Value
            : null;
    }

    private static double[] ParseCoordinateList(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new double[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            result[index] = double.Parse(parts[index], CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static int[] ParseIntegerList(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new int[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            result[index] = int.Parse(parts[index], CultureInfo.InvariantCulture);
        }

        return result;
    }
}
