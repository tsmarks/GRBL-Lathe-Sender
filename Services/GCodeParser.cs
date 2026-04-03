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
        var sourceLines = File.ReadAllLines(filePath);
        var blocks = ParseBlocks(sourceLines);
        var segments = ParseSegments(sourceLines);
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

    private static IReadOnlyList<ToolPathSegment> ParseSegments(IEnumerable<string> sourceLines)
    {
        var segments = new List<ToolPathSegment>();

        double currentX = 0;
        double currentZ = 0;
        var absoluteCoordinates = true;
        var metricUnits = true;
        var motionMode = MotionMode.Rapid;
        var plane = GCodePlane.XZ;

        foreach (var rawLine in sourceLines)
        {
            var line = SanitizeLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            MotionMode? explicitMotionMode = null;
            double? xWord = null;
            double? zWord = null;
            double? iWord = null;
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
                    case 'Z':
                        zWord = ConvertUnits(value, metricUnits);
                        break;
                    case 'I':
                        iWord = ConvertUnits(value, metricUnits);
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

            if (!xWord.HasValue && !zWord.HasValue)
            {
                continue;
            }

            var nextX = xWord.HasValue ? ResolveCoordinate(currentX, xWord.Value, absoluteCoordinates) : currentX;
            var nextZ = zWord.HasValue ? ResolveCoordinate(currentZ, zWord.Value, absoluteCoordinates) : currentZ;

            if (motionMode is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc &&
                plane == GCodePlane.XZ &&
                (iWord.HasValue || kWord.HasValue))
            {
                segments.AddRange(BuildArcSegments(
                    currentX,
                    currentZ,
                    nextX,
                    nextZ,
                    iWord ?? 0,
                    kWord ?? 0,
                    motionMode == MotionMode.ClockwiseArc));
            }
            else if (!AreEqual(currentX, nextX) || !AreEqual(currentZ, nextZ))
            {
                segments.Add(new ToolPathSegment(currentZ, currentX, nextZ, nextX, motionMode == MotionMode.Rapid));
            }

            currentX = nextX;
            currentZ = nextZ;
        }

        return segments;
    }

    private static IEnumerable<ToolPathSegment> BuildArcSegments(
        double startX,
        double startZ,
        double endX,
        double endZ,
        double iOffset,
        double kOffset,
        bool clockwise)
    {
        var centerX = startX + iOffset;
        var centerZ = startZ + kOffset;
        var radius = Math.Sqrt(Math.Pow(startX - centerX, 2) + Math.Pow(startZ - centerZ, 2));

        if (radius < 0.0001)
        {
            yield return new ToolPathSegment(startZ, startX, endZ, endX, false);
            yield break;
        }

        var startAngle = Math.Atan2(startX - centerX, startZ - centerZ);
        var endAngle = Math.Atan2(endX - centerX, endZ - centerZ);
        var sweepAngle = NormalizeSweep(startAngle, endAngle, clockwise);
        var segmentCount = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweepAngle) * radius / 1.5));

        var previousX = startX;
        var previousZ = startZ;

        for (var index = 1; index <= segmentCount; index++)
        {
            var progress = (double)index / segmentCount;
            var angle = startAngle + (sweepAngle * progress);
            var pointX = centerX + (Math.Sin(angle) * radius);
            var pointZ = centerZ + (Math.Cos(angle) * radius);

            yield return new ToolPathSegment(previousZ, previousX, pointZ, pointX, false);

            previousX = pointX;
            previousZ = pointZ;
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
