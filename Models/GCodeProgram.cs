using System.Collections.Generic;
using System.IO;

namespace GRBL_Lathe_Control.Models;

public sealed class GCodeProgram
{
    public GCodeProgram(
        string filePath,
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<GCodeBlock> blocks,
        IReadOnlyList<ToolPathSegment> segments,
        IReadOnlyList<int> toolNumbers,
        int executableLineCount)
    {
        FilePath = filePath;
        SourceLines = sourceLines;
        Blocks = blocks;
        Segments = segments;
        ToolNumbers = toolNumbers;
        ExecutableLineCount = executableLineCount;
    }

    public string FilePath { get; }

    public string DisplayName => Path.GetFileName(FilePath);

    public IReadOnlyList<string> SourceLines { get; }

    public IReadOnlyList<GCodeBlock> Blocks { get; }

    public IReadOnlyList<ToolPathSegment> Segments { get; }

    public IReadOnlyList<int> ToolNumbers { get; }

    public int ExecutableLineCount { get; }
}
