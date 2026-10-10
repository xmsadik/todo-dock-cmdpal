using System;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TodoDock.Core;

namespace TodoDock;

/// <summary>
/// The single Dock ListItem for the extension's lifetime (RaiseItemsChanged does not refresh a
/// WrappedDockItem), so only its properties are mutated, and only when the value changed.
/// </summary>
internal sealed partial class TodoBand : ListItem
{
    private string? _icon;
    private string? _title;
    private string? _subtitle;

    public TodoBand(TasksPage page)
        : base(page)
    {
    }

    public void Render(TaskSnapshot snapshot, string labelFormat, string? saveError = null)
    {
        var icon = LightIcons.Emoji(snapshot.Light);
        var title = snapshot.Label(labelFormat);
        var subtitle = saveError is null ? snapshot.Tooltip : "⚠ Couldn't save tasks: " + ShortReason(saveError);

        // No lock: these setters call into the host (see TodoDockCommandsProvider.Refresh).
        if (icon != _icon)
        {
            try
            {
                Icon = new IconInfo(icon);
                _icon = icon;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"TodoBand icon failed: {ex}");
            }
        }

        if (title != _title)
        {
            try
            {
                Title = title;
                _title = title;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"TodoBand title failed: {ex}");
            }
        }

        if (subtitle != _subtitle)
        {
            try
            {
                Subtitle = subtitle;
                _subtitle = subtitle;
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"TodoBand subtitle failed: {ex}");
            }
        }
    }

    private static string ShortReason(string error)
    {
        var line = error.ReplaceLineEndings(" ").Trim();
        return line.Length > 60 ? line[..57] + "..." : line;
    }
}
