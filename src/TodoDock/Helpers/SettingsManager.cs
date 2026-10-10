using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace TodoDock;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    public const string ProgressFormat = "progress";
    public const string PercentFormat = "percent";
    public const string IconFormat = "icon";

    public const string ModeToday = "today";
    public const string ModeAll = "all";

    private static readonly string _namespace = "TodoDock";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ChoiceSetSetting _labelFormat = new(
        Namespaced(nameof(LabelFormat)),
        "Label format",
        "What the Dock item shows next to the status light",
        [
            new ChoiceSetSetting.Choice("Progress (3/7 · 43%)", ProgressFormat),
            new ChoiceSetSetting.Choice("Percent only (43%)", PercentFormat),
            new ChoiceSetSetting.Choice("Icon only", IconFormat),
        ]);

    private readonly ChoiceSetSetting _progressMode = new(
        Namespaced(nameof(ProgressMode)),
        "Progress counts",
        "Which tasks the Dock progress is calculated from",
        [
            new ChoiceSetSetting.Choice("Today and overdue tasks", ModeToday),
            new ChoiceSetSetting.Choice("All tasks", ModeAll),
        ]);

    /// <summary>"today" (default) or "all"; passed to the task classifier.</summary>
    public string ProgressMode => _progressMode.Value == ModeAll ? ModeAll : ModeToday;

    /// <summary>One of the format constants; anything unknown falls back to <see cref="ProgressFormat"/>.</summary>
    public string LabelFormat => _labelFormat.Value is PercentFormat or IconFormat ? _labelFormat.Value : ProgressFormat;

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("TodoDock");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_labelFormat);
        Settings.Add(_progressMode);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (_, _) =>
        {
            try
            {
                SaveSettings();
            }
            catch (Exception)
            {
                // Persisting failed (locked/readonly settings.json); in-memory values are still correct.
            }
        };
    }
}
