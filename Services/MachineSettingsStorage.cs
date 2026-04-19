using System;
using System.IO;
using System.Text.Json;

namespace GRBL_Lathe_Control.Services;

public sealed record MachineSettingsStorageEntry(
    int SelectedSpindleSpeed,
    int SpindleMaxSpeed,
    string? SelectedWorkCoordinateSystem,
    string XJogFeedInput,
    string YJogFeedInput,
    string ZJogFeedInput,
    string AJogFeedInput,
    string BJogFeedInput,
    double SelectedXJogStep,
    double SelectedYJogStep,
    double SelectedZJogStep,
    double SelectedAJogStep,
    double SelectedBJogStep,
    string ToolChangeXInput,
    string ToolChangeYInput,
    string ToolChangeSafeZInput,
    string? ProbeStartZInput,
    string ProbeTravelInput,
    string ProbeFeedInput,
    string ProbeFineFeedInput,
    string ProbeRetractInput);

public static class MachineSettingsStorage
{
    private const string AppDataFolderName = "GRBL Sender";
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName,
            "machine-settings.json");

    public static MachineSettingsStorageEntry? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        var fileContent = File.ReadAllText(FilePath);
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return null;
        }

        return JsonSerializer.Deserialize<MachineSettingsStorageEntry>(fileContent, JsonSerializerOptions);
    }

    public static void Save(MachineSettingsStorageEntry settings)
    {
        var directoryPath = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var serializedSettings = JsonSerializer.Serialize(settings, JsonSerializerOptions);
        File.WriteAllText(FilePath, serializedSettings);
    }
}
