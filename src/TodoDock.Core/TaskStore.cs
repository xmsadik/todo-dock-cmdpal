using System.Globalization;
using System.Text.Json;

namespace TodoDock.Core;

/// <summary>Thread-safe, file-backed task list with single-level undo.</summary>
public sealed class TaskStore
{
    private const int PurgeAfterDays = 7;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly TimeProvider _time;
    private List<TodoItem> _items = [];
    private List<TodoItem>? _undo;
    private string? _undoDescription;
    private bool _saveDisabled;
    private string? _lastSaveError;
    private string? _pendingSaveError;

    public TaskStore(string path, TimeProvider time)
    {
        _path = path;
        _time = time;
        Load();
    }

    public event EventHandler? Changed;

    /// <summary>Raised (with a short message) when writing tasks.json failed; the next mutation retries.</summary>
    public event EventHandler<string>? SaveFailed;

    /// <summary>Message of the most recent failed save; null once a later save succeeded.</summary>
    public string? LastSaveError
    {
        get
        {
            lock (_gate)
            {
                return _lastSaveError;
            }
        }
    }

    /// <summary>%LOCALAPPDATA%\TodoDock\tasks.json.</summary>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoDock", "tasks.json");

    public IReadOnlyList<TodoItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo is not null;
            }
        }
    }

    /// <summary>Human label of the action Undo would revert (e.g. "Completed 'x'"); null when nothing can be undone.</summary>
    public string? UndoDescription
    {
        get
        {
            lock (_gate)
            {
                return _undo is null ? null : _undoDescription;
            }
        }
    }

    public DateTime LocalNow => _time.GetLocalNow().DateTime;

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public TodoItem Add(ParsedTask parsed)
    {
        TodoItem item;
        lock (_gate)
        {
            item = new TodoItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = parsed.Title,
                Due = parsed.Due,
                Time = parsed.Time,
                Recurrence = parsed.Recurrence,
                RecurrenceDay = AnchorFor(parsed),
                CreatedAt = _time.GetLocalNow(),
            };
            BeginMutation("Added", item.Title);
            _items.Add(item);
            Save();
        }

        RaiseChanged();
        return item;
    }

    public bool Update(string id, ParsedTask parsed) =>
        Mutate(id, "Edited", i => i with
        {
            Title = parsed.Title,
            Due = parsed.Due,
            Time = parsed.Time,
            Recurrence = parsed.Recurrence,
            RecurrenceDay = AnchorFor(parsed),
        });

    /// <summary>Reopens a completed task and removes the open successor its completion spawned.</summary>
    public bool Reopen(string id)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0)
            {
                return false;
            }

            BeginMutation("Reopened", _items[idx].Title);
            _items[idx] = _items[idx] with { CompletedAt = null, CompletedOn = null };
            _items.RemoveAll(i => !i.IsDone && i.Id != id && i.SpawnedFromId == id);
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Due = max(Due, today) + 1 day, time kept. Only this instance moves; the weekly anchor is unchanged.</summary>
    public bool Postpone(string id)
    {
        var today = Today;
        return Mutate(id, "Postponed", i => i with { Due = (i.Due > today ? i.Due : today).AddDays(1) });
    }

    public bool Complete(string id)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0 || _items[idx].IsDone)
            {
                return false;
            }

            var now = _time.GetLocalNow();
            var today = DateOnly.FromDateTime(now.DateTime);
            var item = _items[idx];
            BeginMutation("Completed", item.Title);
            _items[idx] = item with { CompletedAt = now, CompletedOn = today };
            if (item.Recurrence != Recurrence.None)
            {
                _items.Add(new TodoItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = item.Title,
                    Time = item.Time,
                    Recurrence = item.Recurrence,
                    RecurrenceDay = item.Recurrence == Recurrence.Weekly ? item.RecurrenceDay ?? item.Due.DayOfWeek : item.RecurrenceDay,
                    SpawnedFromId = item.Id,
                    Due = NextOccurrence(item, today),
                    CreatedAt = now,
                });
            }

            Save();
        }

        RaiseChanged();
        return true;
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0)
            {
                return false;
            }

            BeginMutation("Deleted", _items[idx].Title);
            _items.RemoveAt(idx);
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Reverts the last mutation exactly (including a spawned recurring instance).</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            if (_undo is null)
            {
                return false;
            }

            _items = _undo;
            _undo = null;
            _undoDescription = null;
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Removes completions older than 7 days (never those completed today). Returns the number removed.</summary>
    public int Purge()
    {
        int removed;
        lock (_gate)
        {
            removed = PurgeCore();
            if (removed > 0)
            {
                _undo = null;
                _undoDescription = null; // the snapshot may reference purged items
                Save();
            }
        }

        if (removed > 0)
        {
            RaiseChanged();
        }

        return removed;
    }

    /// <summary>First occurrence after max(item.Due, today) according to the recurrence rule.</summary>
    internal static DateOnly NextOccurrence(TodoItem item, DateOnly today)
    {
        var baseDay = item.Due > today ? item.Due : today;
        switch (item.Recurrence)
        {
            case Recurrence.Daily:
                return baseDay.AddDays(1);
            case Recurrence.Weekdays:
                var d = baseDay.AddDays(1);
                while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    d = d.AddDays(1);
                }

                return d;
            case Recurrence.Weekly:
                var anchor = item.RecurrenceDay ?? item.Due.DayOfWeek;
                var w = baseDay.AddDays(1);
                while (w.DayOfWeek != anchor)
                {
                    w = w.AddDays(1);
                }

                return w;
            default:
                return item.Due;
        }
    }

    private bool Mutate(string id, string verb, Func<TodoItem, TodoItem> change)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0)
            {
                return false;
            }

            BeginMutation(verb, _items[idx].Title);
            _items[idx] = change(_items[idx]);
            Save();
        }

        RaiseChanged();
        return true;
    }

    private void BeginMutation(string verb, string title)
    {
        _undo = [.. _items];
        _undoDescription = $"{verb} '{title}'";
    }

    private int PurgeCore()
    {
        var cutoff = DateOnly.FromDateTime(_time.GetLocalNow().DateTime).AddDays(-PurgeAfterDays);
        return _items.RemoveAll(i => i.CompletedOn is { } c && c < cutoff);
    }

    private static DayOfWeek? AnchorFor(ParsedTask parsed) =>
        parsed.Recurrence == Recurrence.Weekly ? parsed.RecurrenceDay ?? parsed.Due.DayOfWeek : null;

    /// <summary>Raises SaveFailed (if a save failed under the lock) then Changed; a throwing handler never propagates.</summary>
    private void RaiseChanged()
    {
        string? failure;
        lock (_gate)
        {
            failure = _pendingSaveError;
            _pendingSaveError = null;
        }

        if (failure is not null)
        {
            InvokeSafe(SaveFailed, failure);
        }

        InvokeSafe(Changed);
    }

    private void InvokeSafe(EventHandler<string>? handlers, string arg)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var h in handlers.GetInvocationList().Cast<EventHandler<string>>())
        {
            try
            {
                h(this, arg);
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"store event handler threw: {ex}");
            }
        }
    }

    private void InvokeSafe(EventHandler? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var h in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                h(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"store event handler threw: {ex}");
            }
        }
    }

    private void Load()
    {
        lock (_gate)
        {
            bool repaired;
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var json = File.ReadAllText(_path);
                var file = JsonSerializer.Deserialize(json, TodoJsonContext.Default.TodoFile)
                    ?? throw new JsonException("Empty document.");
                _items = Sanitize(file.Tasks, out repaired);
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"tasks load failed: {ex.Message}");
                _items = [];
                if (!QuarantineCorruptFile())
                {
                    // Never overwrite a file we could neither read nor move aside.
                    _saveDisabled = true;
                    DiagLog.WriteLine("saving disabled for this session (corrupt tasks file could not be quarantined)");
                }

                return;
            }

            if (PurgeCore() > 0 || repaired)
            {
                Save();
            }
        }
    }

    private static List<TodoItem> Sanitize(List<TodoItem?>? loaded, out bool repaired)
    {
        repaired = false;
        var result = new List<TodoItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in loaded ?? [])
        {
            if (raw is null)
            {
                repaired = true;
                continue;
            }

            var item = raw;
            if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id))
            {
                item = item with { Id = NewId(seen) };
                repaired = true;
            }

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                item = item with { Title = "(untitled)" };
                repaired = true;
            }

            if (item.CompletedAt is { } at && item.CompletedOn is null)
            {
                item = item with { CompletedOn = DateOnly.FromDateTime(at.DateTime) };
                repaired = true;
            }

            if (!Enum.IsDefined(item.Recurrence))
            {
                item = item with { Recurrence = Recurrence.None };
                repaired = true;
            }

            if (item.RecurrenceDay is { } d && !Enum.IsDefined(d))
            {
                item = item with { RecurrenceDay = null };
                repaired = true;
            }

            result.Add(item);
        }

        return result;
    }

    private static string NewId(HashSet<string> seen)
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N");
        }
        while (!seen.Add(id));
        return id;
    }

    /// <summary>Moves the unreadable file aside without overwriting anything. Returns false if that failed.</summary>
    private bool QuarantineCorruptFile()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var target = _path + ".bad-" + stamp + (attempt == 0 ? "" : "-" + attempt);
            try
            {
                if (File.Exists(target))
                {
                    continue;
                }

                File.Move(_path, target, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(target))
            {
                // Lost a race for the name; try the next suffix.
            }
            catch (Exception ex)
            {
                DiagLog.WriteLine($"could not quarantine corrupt tasks file: {ex.Message}");
                return false;
            }
        }

        DiagLog.WriteLine("could not quarantine corrupt tasks file: no free name");
        return false;
    }

    private void Save()
    {
        if (_saveDisabled)
        {
            Fail("Saving is disabled because the existing tasks file could not be read or moved aside.");
            return;
        }

        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(new TodoFile { Tasks = [.. _items] }, TodoJsonContext.Default.TodoFile);
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(true);
            }

            File.Move(tmp, _path, overwrite: true);
            _lastSaveError = null;
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception)
            {
                // Best effort.
            }

            DiagLog.WriteLine($"tasks save failed: {ex.Message}");
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        _lastSaveError = message;
        _pendingSaveError = message;
    }
}
