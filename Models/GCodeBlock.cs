namespace GRBL_Lathe_Control.Models;

public sealed class GCodeBlock
{
    public GCodeBlock(string sourceLine, string sanitizedLine, string commandLine, int? toolNumber, bool isPauseCommand)
    {
        SourceLine = sourceLine;
        SanitizedLine = sanitizedLine;
        CommandLine = commandLine;
        ToolNumber = toolNumber;
        IsPauseCommand = isPauseCommand;
    }

    public string SourceLine { get; }

    public string SanitizedLine { get; }

    public string CommandLine { get; }

    public int? ToolNumber { get; }

    public bool IsPauseCommand { get; }

    public bool ShouldSendToController => !string.IsNullOrWhiteSpace(CommandLine);
}
