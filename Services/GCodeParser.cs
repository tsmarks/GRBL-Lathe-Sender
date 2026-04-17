using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GRBL_Lathe_Control.Models;

namespace GRBL_Lathe_Control.Services;

public static class GCodeParser
{
    private static readonly Regex ParenthesisCommentRegex = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"([A-Za-z])([+\-]?(?:\d+(?:\.\d*)?|\.\d+))", RegexOptions.Compiled);

    public static GCodeProgram ParseFile(string filePath)
    {
        return ParseFile(filePath, MachineMode.Lathe);
    }

    public static GCodeProgram ParseFile(string filePath, MachineMode machineMode)
    {
        var sourceLines = File.ReadAllLines(filePath);
        var blocks = ParseBlocks(sourceLines);
        var segments = ParseSegments(sourceLines, machineMode);
        var toolNumbers = blocks
            .Where(block => block.ToolNumber.HasValue)
            .Select(block => block.ToolNumber!.Value)
            .Distinct()
            .OrderBy(toolNumber => toolNumber)
            .ToArray();
        var executableLineCount = blocks.Count(block => block.ShouldSendToController);

        return new GCodeProgram(filePath, sourceLines, blocks, segments, toolNumbers, executableLineCount);
    }

    public static string SanitizeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var withoutParenthesisComments = ParenthesisCommentRegex.Replace(line, string.Empty);
        var semicolonCommentIndex = withoutParenthesisComments.IndexOf(';');
        if (semicolonCommentIndex >= 0)
        {
            withoutParenthesisComments = withoutParenthesisComments[..semicolonCommentIndex];
        }

        return withoutParenthesisComments.Trim().ToUpperInvariant();
    }

    private static IReadOnlyList<GCodeBlock> ParseBlocks(IEnumerable<string> sourceLines)
    {
        var blocks = new List<GCodeBlock>();

        foreach (var sourceLine in sourceLines)
        {
            var sanitizedLine = SanitizeLine(sourceLine);
            if (string.IsNullOrWhiteSpace(sanitizedLine))
            {
                continue;
            }

            var toolNumber = ExtractToolNumber(sanitizedLine);
            var commandLine = BuildCommandLine(sanitizedLine);
            var isPauseCommand = ContainsPauseCommand(commandLine);

            blocks.Add(new GCodeBlock(sourceLine, sanitizedLine, commandLine, toolNumber, isPauseCommand));
        }

        return blocks;
    }

    private static IReadOnlyList<ToolPathSegment> ParseSegments(IEnumerable<string> sourceLines, MachineMode machineMode)
    {
        var segments = new List<ToolPathSegment>();

        double currentX = 0;
        double currentY = 0;
        double currentZ = 0;
        var absoluteCoordinates = true;
        var metricUnits = true;
        var motionMode = MotionMode.Rapid;
        var plane = GCodePlane.XZ;
        var targetPlane = machineMode == MachineMode.Lathe ? GCodePlane.XZ : GCodePlane.XY;

        foreach (var rawLine in sourceLines)
        {
            var line = SanitizeLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            MotionMode? explicitMotionMode = null;
            double? xWord = null;
            double? yWord = null;
            double? zWord = null;
            double? iWord = null;
            double? jWord = null;
            double? kWord = null;

            foreach (Match match in TokenRegex.Matches(line))
            {
                if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                switch (match.Groups[1].Value[0])
                {
                    case 'G':
                    {
                        var gCode = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                        switch (gCode)
                        {
                            case 0:
                                explicitMotionMode = MotionMode.Rapid;
                                break;
                            case 1:
                                explicitMotionMode = MotionMode.Linear;
                                break;
                            case 2:
                                explicitMotionMode = MotionMode.ClockwiseArc;
                                break;
                            case 3:
                                explicitMotionMode = MotionMode.CounterClockwiseArc;
                                break;
                            case 17:
                                plane = GCodePlane.XY;
                                break;
                            case 18:
                                plane = GCodePlane.XZ;
                                break;
                            case 19:
                                plane = GCodePlane.YZ;
                                break;
                            case 20:
                                metricUnits = false;
                                break;
                            case 21:
                                metricUnits = true;
                                break;
                            case 90:
                                absoluteCoordinates = true;
                                break;
                            case 91:
                                absoluteCoordinates = false;
                                break;
                        }

                        break;
                    }
                    case 'X':
                        xWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'Y':
                        yWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'Z':
                        zWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'I':
                        iWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'J':
                        jWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'K':
                        kWord = ConvertUnits(value, metricUnits);
                        break;
                }
            }

            if (explicitMotionMode.HasValue)
            {
                motionMode = explicitMotionMode.Value;
            }

            var nextX = xWord.HasValue ? ResolveCoordinate(currentX, xWord.Value, absoluteCoordinates) : currentX;
            var nextY = yWord.HasValue ? ResolveCoordinate(currentY, yWord.Value, absoluteCoordinates) : currentY;
            var nextZ = zWord.HasValue ? ResolveCoordinate(currentZ, zWord.Value, absoluteCoordinates) : currentZ;

            if (motionMode is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc &&
                plane == GCodePlane.XZ &&
                targetPlane == GCodePlane.XZ &&
                (xWord.HasValue || zWord.HasValue) &&
                (iWord.HasValue || kWord.HasValue))
            {
                segments.AddRange(BuildArcSegments(
                    currentZ,
                    currentX,
                    nextZ,
                    nextX,
                    kWord ?? 0,
                    iWord ?? 0,
                    motionMode == MotionMode.ClockwiseArc));
            }
            else if (motionMode is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc &&
                plane == GCodePlane.XY &&
                targetPlane == GCodePlane.XY &&
                (xWord.HasValue || yWord.HasValue) &&
                (iWord.HasValue || jWord.HasValue))
            {
                segments.AddRange(BuildArcSegments(
                    currentX,
                    currentY,
                    nextX,
                    nextY,
                    iWord ?? 0,
                    jWord ?? 0,
                    motionMode == MotionMode.ClockwiseArc));
            }
            else if (targetPlane == GCodePlane.XZ &&
                     (xWord.HasValue || zWord.HasValue) &&
                     (!AreEqual(currentX, nextX) || !AreEqual(currentZ, nextZ)))
            {
                segments.Add(new ToolPathSegment(currentZ, currentX, nextZ, nextX, motionMode == MotionMode.Rapid));
            }
            else if (targetPlane == GCodePlane.XY &&
                     (xWord.HasValue || yWord.HasValue) &&
                     (!AreEqual(currentX, nextX) || !AreEqual(currentY, nextY)))
            {
                segments.Add(new ToolPathSegment(currentX, currentY, nextX, nextY, motionMode == MotionMode.Rapid));
            }

            currentX = nextX;
            currentY = nextY;
            currentZ = nextZ;
        }

        return segments;
    }

    private static IEnumerable<ToolPathSegment> BuildArcSegments(
        double startHorizontal,
        double startVertical,
        double endHorizontal,
        double endVertical,
        double horizontalOffset,
        double verticalOffset,
        bool clockwise)
    {
        var centerHorizontal = startHorizontal + horizontalOffset;
        var centerVertical = startVertical + verticalOffset;
        var radius = Math.Sqrt(Math.Pow(startHorizontal - centerHorizontal, 2) + Math.Pow(startVertical - centerVertical, 2));

        if (radius < 0.0001)
        {
            yield return new ToolPathSegment(startHorizontal, startVertical, endHorizontal, endVertical, false);
            yield break;
        }

        var startAngle = Math.Atan2(startVertical - centerVertical, startHorizontal - centerHorizontal);
        var endAngle = Math.Atan2(endVertical - centerVertical, endHorizontal - centerHorizontal);
        var sweepAngle = NormalizeSweep(startAngle, endAngle, clockwise);
        var segmentCount = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweepAngle) * radius / 1.5));

        var previousHorizontal = startHorizontal;
        var previousVertical = startVertical;

        for (var index = 1; index <= segmentCount; index++)
        {
            var progress = (double)index / segmentCount;
            var angle = startAngle + (sweepAngle * progress);
            var pointHorizontal = centerHorizontal + (Math.Cos(angle) * radius);
            var pointVertical = centerVertical + (Math.Sin(angle) * radius);

            yield return new ToolPathSegment(previousHorizontal, previousVertical, pointHorizontal, pointVertical, false);

            previousHorizontal = pointHorizontal;
            previousVertical = pointVertical;
        }
    }

    private static double ConvertUnits(double value, bool metricUnits)
    {
        return metricUnits ? value : value * 25.4;
    }

    private static double ResolveCoordinate(double currentValue, double wordValue, bool absoluteCoordinates)
    {
        return absoluteCoordinates ? wordValue : currentValue + wordValue;
    }

    private static double NormalizeSweep(double startAngle, double endAngle, bool clockwise)
    {
        var sweep = endAngle - startAngle;

        if (clockwise)
        {
            while (sweep >= 0)
            {
                sweep -= Math.PI * 2;
            }
        }
        else
        {
            while (sweep <= 0)
            {
                sweep += Math.PI * 2;
            }
        }

        return sweep;
    }

    private static bool AreEqual(double left, double right)
    {
        return Math.Abs(left - right) < 0.0001;
    }

    private static int? ExtractToolNumber(string sanitizedLine)
    {
        foreach (Match match in TokenRegex.Matches(sanitizedLine))
        {
            if (match.Groups[1].Value[0] != 'T')
            {
                continue;
            }

            return NormalizeToolNumber(match.Groups[2].Value);
        }

        return null;
    }

    private static string BuildCommandLine(string sanitizedLine)
    {
        var commandTokens = new List<string>();

        foreach (Match match in TokenRegex.Matches(sanitizedLine))
        {
            var letter = match.Groups[1].Value[0];
            var rawValue = match.Groups[2].Value;

            if (letter == 'T')
            {
                continue;
            }

            if (letter == 'M' &&
                int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mCode) &&
                mCode == 6)
            {
                continue;
            }

            commandTokens.Add(match.Value);
        }

        return string.Concat(commandTokens);
    }

    private static bool ContainsPauseCommand(string commandLine)
    {
        foreach (Match match in TokenRegex.Matches(commandLine))
        {
            if (match.Groups[1].Value[0] != 'M')
            {
                continue;
            }

            if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mCode) &&
                (mCode == 0 || mCode == 1))
            {
                return true;
            }
        }

        return false;
    }

    private static int NormalizeToolNumber(string rawValue)
    {
        var unsignedValue = rawValue.TrimStart('+');
        var decimalIndex = unsignedValue.IndexOf('.');
        var wholeDigits = decimalIndex >= 0 ? unsignedValue[..decimalIndex] : unsignedValue;
        wholeDigits = wholeDigits.TrimStart('0');

        if (string.IsNullOrEmpty(wholeDigits))
        {
            wholeDigits = "0";
        }

        var originalWholeDigits = decimalIndex >= 0 ? rawValue.TrimStart('+')[..decimalIndex] : rawValue.TrimStart('+');
        if (originalWholeDigits.Length >= 4 && originalWholeDigits.Length % 2 == 0)
        {
            var turretDigits = originalWholeDigits[..(originalWholeDigits.Length / 2)].TrimStart('0');
            return string.IsNullOrEmpty(turretDigits)
                ? 0
                : int.Parse(turretDigits, CultureInfo.InvariantCulture);
        }

        return int.Parse(wholeDigits, CultureInfo.InvariantCulture);
    }

    private enum MotionMode
    {
        Rapid,
        Linear,
        ClockwiseArc,
        CounterClockwiseArc
    }

    private enum GCodePlane
    {
        XY,
        XZ,
        YZ
    }
}
