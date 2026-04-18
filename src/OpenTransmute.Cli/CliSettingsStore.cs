using System.Text.Json;

namespace OpenTransmute.Cli;

/// <summary>
/// Loads and saves <see cref="CliSettings"/> to <c>~/.opentransmute/settings.json</c>.
/// </summary>
public sealed class CliSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".opentransmute",
        "settings.json");

    public CliSettings Load()
    {
        if (!File.Exists(Path)) return new CliSettings();
        try
        {
            return JsonSerializer.Deserialize<CliSettings>(File.ReadAllText(Path), JsonOptions)
                   ?? new CliSettings();
        }
        catch
        {
            return new CliSettings();
        }
    }

    public void Save(CliSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
