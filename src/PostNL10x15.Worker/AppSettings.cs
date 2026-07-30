using System.Text.Json;
using Microsoft.Win32;

namespace PostNL10x15.Worker;

public sealed record AppSettings
{
    public string TargetPrinter { get; init; } =
        "ZDesigner ZD220-203dpi ZPL";

    public string ZebraPrinterHint { get; init; } =
        "ZDesigner ZD220-203dpi ZPL";

    public double LabelWidthMm { get; init; } = 150;

    public double LabelHeightMm { get; init; } = 100;

    public int RenderDotsPerMillimeter { get; init; } = 8;

    public int RenderDpi => (int)Math.Round(
        RenderDotsPerMillimeter * 25.4,
        MidpointRounding.AwayFromZero);

    public static AppSettings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppSettings settings;
        if (!File.Exists(path))
        {
            settings = new AppSettings();
        }
        else
        {
            string json = File.ReadAllText(path);
            settings = JsonSerializer.Deserialize<AppSettings>(
                           json,
                           new JsonSerializerOptions
                           {
                               PropertyNameCaseInsensitive = true
                           })
                       ?? new AppSettings();
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\PostNL10x15");
        string? selectedPrinter = key?.GetValue("TargetPrinter") as string;

        return string.IsNullOrWhiteSpace(selectedPrinter)
            ? settings
            : settings with { TargetPrinter = selectedPrinter.Trim() };
    }
}
