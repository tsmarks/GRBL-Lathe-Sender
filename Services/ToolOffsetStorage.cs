using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GRBL_Lathe_Control.Services;

public sealed record ToolOffsetStorageEntry(int ToolNumber, string XOffsetInput, string ZOffsetInput);

public static class ToolOffsetStorage
{
    private const string HeaderLine = "ToolNumber,XOffset,ZOffset";

    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GRBL Lathe Control",
            "tools.csv");

    public static IReadOnlyList<ToolOffsetStorageEntry> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var entries = new List<ToolOffsetStorageEntry>();
        foreach (var line in File.ReadLines(FilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line);
            if (columns.Count == 0)
            {
                continue;
            }

            if (string.Equals(columns[0], "ToolNumber", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (columns.Count < 3 ||
                !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var toolNumber) ||
                toolNumber <= 0)
            {
                continue;
            }

            entries.Add(new ToolOffsetStorageEntry(
                toolNumber,
                columns[1],
                columns[2]));
        }

        return entries
            .GroupBy(entry => entry.ToolNumber)
            .Select(group => group.Last())
            .OrderBy(entry => entry.ToolNumber)
            .ToArray();
    }

    public static void Save(IEnumerable<ToolOffsetStorageEntry> entries)
    {
        var directoryPath = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var lines = new List<string> { HeaderLine };
        lines.AddRange(entries
            .OrderBy(entry => entry.ToolNumber)
            .Select(entry => string.Join(",",
                entry.ToolNumber.ToString(CultureInfo.InvariantCulture),
                EscapeCsvValue(entry.XOffsetInput),
                EscapeCsvValue(entry.ZOffsetInput))));

        File.WriteAllLines(FilePath, lines, Encoding.UTF8);
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var columns = new List<string>();
        var currentValue = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var currentCharacter = line[index];

            if (currentCharacter == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    currentValue.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (currentCharacter == ',' && !inQuotes)
            {
                columns.Add(currentValue.ToString());
                currentValue.Clear();
                continue;
            }

            currentValue.Append(currentCharacter);
        }

        columns.Add(currentValue.ToString());
        return columns;
    }
}
