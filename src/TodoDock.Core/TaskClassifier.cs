using System.Globalization;

namespace TodoDock.Core;

public enum Light
{
    None,
    Red,
    Orange,
    Green,
}

public static class LightIcons
{
    public static string Emoji(Light light) => light switch
    {
        Light.Red => "🔴",
        Light.Orange => "🟠",
        Light.Green => "🟢",
        _ => "⚪",
    };
}

/// <summary>Classification of tasks relative to a local "now".</summary>
public static class TaskClassifier
{
    public const string ModeToday = "today";
    public const string ModeAll = "all";

    public static bool IsOverdue(TodoItem item, DateTime now)
    {
        if (item.IsDone)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(now);
        if (item.Due < today)
        {
            return true;
        }

        return item.Due == today && item.Time is { } t && t < TimeOnly.FromTimeSpan(now.TimeOfDay);
    }

    public static TaskSnapshot Classify(IEnumerable<TodoItem> items, DateTime now, string mode = ModeToday)
    {
        var all = items.ToList();
        var today = DateOnly.FromDateTime(now);
        var open = all.Where(i => !i.IsDone).ToList();

        var overdue = open.Where(i => IsOverdue(i, now))
            .OrderBy(i => i.Due).ThenBy(i => i.Time ?? TimeOnly.MinValue).ThenBy(i => i.CreatedAt).ToList();
        var todayList = open.Where(i => i.Due == today && !IsOverdue(i, now))
            .OrderBy(i => i.Time is null ? 1 : 0).ThenBy(i => i.Time ?? TimeOnly.MinValue).ThenBy(i => i.CreatedAt).ToList();
        var upcoming = open.Where(i => i.Due > today)
            .OrderBy(i => i.Due).ThenBy(i => i.Time ?? TimeOnly.MinValue).ThenBy(i => i.CreatedAt).ToList();
        var done = all.Where(i => i.IsDone).OrderByDescending(i => i.CompletedAt).ToList();

        int progDone, progTotal;
        if (string.Equals(mode, ModeAll, StringComparison.OrdinalIgnoreCase))
        {
            progDone = done.Count;
            progTotal = all.Count;
        }
        else
        {
            progDone = done.Count(i => i.CompletedOn == today);
            progTotal = progDone + open.Count(i => i.Due <= today);
        }

        // The light always reasons about today, regardless of the progress mode.
        var todayDone = done.Count(i => i.CompletedOn == today);
        var todayTotal = todayDone + open.Count(i => i.Due <= today);
        var light = overdue.Count > 0 ? Light.Red
            : todayList.Count > 0 ? Light.Orange
            : todayTotal > 0 ? Light.Green
            : Light.None;

        return new TaskSnapshot(overdue, todayList, upcoming, done, progDone, progTotal, light);
    }
}

public sealed class TaskSnapshot
{
    internal TaskSnapshot(
        IReadOnlyList<TodoItem> overdue,
        IReadOnlyList<TodoItem> today,
        IReadOnlyList<TodoItem> upcoming,
        IReadOnlyList<TodoItem> done,
        int progressDone,
        int progressTotal,
        Light light)
    {
        Overdue = overdue;
        Today = today;
        Upcoming = upcoming;
        Done = done;
        ProgressDone = progressDone;
        ProgressTotal = progressTotal;
        Light = light;
    }

    public IReadOnlyList<TodoItem> Overdue { get; }

    public IReadOnlyList<TodoItem> Today { get; }

    public IReadOnlyList<TodoItem> Upcoming { get; }

    public IReadOnlyList<TodoItem> Done { get; }

    public int ProgressDone { get; }

    public int ProgressTotal { get; }

    public Light Light { get; }

    /// <summary>Rounded completion percentage; null when there is nothing to measure.</summary>
    public int? Percent => ProgressTotal == 0
        ? null
        : (int)Math.Round(ProgressDone * 100.0 / ProgressTotal, MidpointRounding.AwayFromZero);

    /// <summary>"progress" → "3/7 · 43%"; "percent" → "43%"; "icon" → ""; empty when total is 0.</summary>
    public string Label(string format)
    {
        if (ProgressTotal == 0 || Percent is not { } p)
        {
            return "";
        }

        return format.ToLowerInvariant() switch
        {
            "progress" => string.Create(CultureInfo.InvariantCulture, $"{ProgressDone}/{ProgressTotal} · {p}%"),
            "percent" => string.Create(CultureInfo.InvariantCulture, $"{p}%"),
            _ => "",
        };
    }

    public string Tooltip
    {
        get
        {
            if (Overdue.Count > 0)
            {
                return "Overdue: " + Overdue[0].Title + (Overdue.Count > 1 ? $" (+{Overdue.Count - 1} more)" : "");
            }

            if (Today.Count > 0)
            {
                var next = Today[0];
                return "Next: " + next.Title
                    + (next.Time is { } t ? " at " + t.ToString("HH:mm", CultureInfo.InvariantCulture) : "");
            }

            return Light == Light.Green ? "All done for today" : "No tasks for today";
        }
    }

    /// <summary>"2 overdue · 3 today · 4 upcoming".</summary>
    public string CountsLine => string.Create(
        CultureInfo.InvariantCulture, $"{Overdue.Count} overdue · {Today.Count} today · {Upcoming.Count} upcoming");
}
