using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TodoDock.Core;

namespace TodoDock;

internal sealed partial class TasksPage : DynamicListPage
{
    private readonly TaskStore _store;
    private readonly SettingsManager _settings;
    private readonly object _sync = new(); // guards _lastSubmitted; never held across host calls
    private string? _lastSubmitted;

    public TasksPage(TaskStore store, SettingsManager settings)
    {
        _store = store;
        _settings = settings;
        Id = "TodoDock.tasks";
        Name = "Open";
        Title = "Todo Dock";
        PlaceholderText = "Add a task: e.g. \"pay bill tomorrow 9:30\"";
    }

    /// <summary>Re-fetches the list. Driven by the provider's signature check (single refresh path).</summary>
    public void Refresh()
    {
        try
        {
            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"TasksPage refresh failed: {ex}");
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch == newSearch)
        {
            return;
        }

        lock (_sync)
        {
            if (newSearch?.Trim() != _lastSubmitted)
            {
                _lastSubmitted = null;
            }
        }

        Refresh();
    }

    /// <summary>
    /// Re-reads the live search text and adds it. No host call (SearchText setter) may run while
    /// <see cref="_sync"/> is held: the host answers PropChanged by calling UpdateSearchText on another
    /// thread, which also takes the lock, and that deadlocks the extension.
    /// </summary>
    internal void Submit()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var parsed = QuickAddParser.Parse(text, _store.Today);
        if (parsed is null)
        {
            return;
        }

        lock (_sync)
        {
            if (text == _lastSubmitted)
            {
                return; // a second Enter before the cleared text came back
            }

            _lastSubmitted = text;
        }

        _store.Add(parsed);
        SetSearchText(string.Empty);
    }

    private void SetSearchText(string text)
    {
        try
        {
            SearchText = text;

            // LOAD BEARING: the toolkit setter raises no PropChanged, so the host's search box would keep
            // the old text. Same workaround as the PowerToys Calculator extension (CalculatorListPage).
            OnPropertyChanged(nameof(SearchText));
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Setting SearchText failed: {ex}");
        }
    }

    public override IListItem[] GetItems()
    {
        try
        {
            return Build();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"GetItems failed: {ex}");
            return [new ListItem(new NoOpCommand()) { Title = "Something went wrong" }];
        }
    }

    private IListItem[] Build()
    {
        var now = _store.LocalNow;
        var today = DateOnly.FromDateTime(now);
        var snapshot = TaskClassifier.Classify(_store.Items, now, _settings.ProgressMode);
        var items = new List<IListItem>();
        var text = SearchText?.Trim() ?? string.Empty;

        if (text.Length > 0)
        {
            var parsed = QuickAddParser.Parse(text, today);
            if (parsed is not null)
            {
                items.Add(new ListItem(new AddTaskCommand(this, text))
                {
                    Title = "Add: " + parsed.Title,
                    Subtitle = parsed.Description,
                    Icon = new IconInfo("➕"),
                });
            }
            else
            {
                items.Add(new ListItem(new NoOpCommand()) { Title = "Type a task title" });
            }
        }

        var rowsBefore = items.Count;
        AddSection(items, "Overdue", snapshot.Overdue, "🔴", today, overdue: true);
        AddSection(items, "Today", snapshot.Today, "🟠", today, overdue: false);
        AddSection(items, "Upcoming", snapshot.Upcoming, "⚪", today, overdue: false);

        var doneToday = new List<TodoItem>();
        foreach (var d in snapshot.Done)
        {
            if (d.CompletedOn == today)
            {
                doneToday.Add(d);
            }
        }

        AddSection(items, "Done today", doneToday, "✅", today, overdue: false);

        var taskRows = items.Count - rowsBefore;
        if (text.Length == 0)
        {
            if (taskRows == 0)
            {
                items.Add(new ListItem(new NoOpCommand()) { Title = "No tasks yet — type to add one" });
            }

            if (_store.UndoDescription is { } undo)
            {
                items.Add(new ListItem(new UndoCommand(_store, this))
                {
                    Title = "Undo: " + undo,
                    Icon = new IconInfo("↶"),
                });
            }

            items.Add(new ListItem(_settings.Settings.SettingsPage)
            {
                Title = "Settings",
                Icon = new IconInfo("⚙️"),
            });
        }

        return [.. items];
    }

    private void AddSection(
        List<IListItem> items, string section, IReadOnlyList<TodoItem> tasks, string icon, DateOnly today, bool overdue)
    {
        foreach (var task in tasks)
        {
            var done = task.IsDone;
            ICommand primary = done
                ? new ReopenTaskCommand(_store, this, task.Id)
                : new CompleteTaskCommand(_store, this, task.Id);

            var more = new List<IContextItem>();
            if (!done)
            {
                more.Add(new CommandContextItem(new PostponeTaskCommand(_store, this, task.Id)));
            }

            more.Add(new CommandContextItem(new EditTaskPage(_store, task.Id)));
            more.Add(new CommandContextItem(new DeleteTaskCommand(_store, this, task.Id)));

            var item = new ListItem(primary)
            {
                Title = task.Title,
                Subtitle = DueText(task, today, overdue),
                Icon = new IconInfo(icon),
                Section = section,
                MoreCommands = [.. more],
            };

            if (overdue)
            {
                item.Tags =
                [
                    new Tag("Overdue")
                    {
                        Foreground = ColorHelpers.FromRgb(255, 255, 255),
                        Background = ColorHelpers.FromRgb(196, 43, 28),
                    },
                ];
            }

            items.Add(item);
        }
    }

    internal static string DueText(TodoItem task, DateOnly today, bool overdue)
    {
        var inv = CultureInfo.InvariantCulture;
        var time = task.Time is { } t ? t.ToString("HH:mm", inv) : null;
        string text;

        if (task.IsDone)
        {
            text = task.CompletedAt is { } c ? "Completed " + c.ToLocalTime().ToString("HH:mm", inv) : "Completed";
        }
        else if (overdue)
        {
            text = task.Due == today && time is not null
                ? "Overdue since " + time
                : "Overdue since " + task.Due.ToString("MMM d", inv);
        }
        else if (task.Due == today)
        {
            text = time ?? "Today";
        }
        else if (task.Due == today.AddDays(1))
        {
            text = time is null ? "Tomorrow" : "Tomorrow · " + time;
        }
        else
        {
            text = task.Due.ToString("ddd, MMM d", inv) + (time is null ? "" : " · " + time);
        }

        return text + task.Recurrence switch
        {
            Recurrence.Daily => " · every day",
            Recurrence.Weekdays => " · weekdays",
            Recurrence.Weekly => " · every week",
            _ => "",
        };
    }
}
