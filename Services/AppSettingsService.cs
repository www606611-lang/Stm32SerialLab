using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stm32SerialLab.Services;

internal sealed class AppSettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stm32SerialLab");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private AppSettingsService()
    {
        Values = Load();
    }

    public static AppSettingsService Current { get; } = new();

    public AppSettings Values { get; }

    public void Save()
    {
        string temporaryPath = SettingsPath + ".tmp";

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Values, AppSettingsJsonContext.Default.AppSettings));
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch (IOException)
        {
            TryDelete(temporaryPath);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            AppSettings settings = JsonSerializer.Deserialize(
                File.ReadAllText(SettingsPath),
                AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            settings.MetricVisibility ??= [];
            return settings;
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class AppSettings
{
    public string Theme { get; set; } = "Default";
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public string SerialPort { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
    public bool DisplayHex { get; set; }
    public int SendModeIndex { get; set; }
    public int LineEndingIndex { get; set; } = 2;
    public bool AutoScroll { get; set; } = true;
    public bool DemoEnabled { get; set; } = true;
    public int WorkspaceTabIndex { get; set; }
    public int ScopeTimeWindowIndex { get; set; } = 6;
    public int ScopeVerticalGainIndex { get; set; } = 4;
    public string ScopeYAxisMode { get; set; } = "Follow";
    public Dictionary<string, bool>? MetricVisibility { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
