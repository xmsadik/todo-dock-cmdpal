using TodoDock.Core;
using Xunit;

namespace TodoDock.Tests;

/// <summary>Regression tests for the Todo Dock core review findings (store robustness).</summary>
public sealed class ReviewFixStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TodoDockFix-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTime(2026, 10, 10, 14, 0, 0)); // Saturday
    private string FilePath => Path.Combine(_dir, "tasks.json");

    public ReviewFixStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private TaskStore Store() => new(FilePath, _time);

    private ParsedTask P(string text) => QuickAddParser.Parse(text, _time.Today)!;

    private string[] BadFiles() => Directory.GetFiles(_dir, "tasks.json.bad-*");

    [Fact]
    public void Null_tasks_loads_as_empty_without_quarantine()
    {
        File.WriteAllText(FilePath, """{"tasks":null}""");
        var s = Store();
        Assert.Empty(s.Items);
        Assert.Empty(BadFiles());
        Assert.True(File.Exists(FilePath));
        s.Add(P("works"));
        Assert.Single(Store().Items);
    }

    [Fact]
    public void Any_load_exception_quarantines_and_starts_empty()
    {
        File.WriteAllText(FilePath, """{"tasks":[{"due":"not-a-date"}]}""");
        var s = Store();
        Assert.Empty(s.Items);
        Assert.Single(BadFiles());
        s.Add(P("works"));
        Assert.Single(Store().Items);
    }

    [Fact]
    public void Quarantine_never_overwrites_an_existing_bad_file()
    {
        File.WriteAllText(FilePath, "{ broken");
        var first = $"{FilePath}.bad-20261010140000000";
        File.WriteAllText(first, "precious");
        var s = Store();
        Assert.Empty(s.Items);
        Assert.Equal("precious", File.ReadAllText(first));
        Assert.Equal(2, BadFiles().Length);
    }

    [Fact]
    public void Failed_quarantine_disables_saving_so_the_file_is_never_overwritten()
    {
        File.WriteAllText(FilePath, "{ broken");
        TaskStore s;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            s = Store(); // read and move both fail while the file is locked
        }

        Assert.Empty(s.Items);
        string? raised = null;
        s.SaveFailed += (_, m) => raised = m;
        s.Add(P("a"));
        Assert.Equal("{ broken", File.ReadAllText(FilePath));
        Assert.NotNull(s.LastSaveError);
        Assert.Equal(s.LastSaveError, raised);
        Assert.Single(s.Items); // in-memory state is kept
    }

    [Fact]
    public void Save_failure_keeps_state_raises_event_cleans_temp_and_next_mutation_retries()
    {
        var s = Store();
        Directory.CreateDirectory(FilePath); // a directory where the file should go: the final move fails
        var messages = new List<string>();
        s.SaveFailed += (_, m) => messages.Add(m);

        var a = s.Add(P("a"));
        Assert.Single(messages);
        Assert.Equal(messages[0], s.LastSaveError);
        Assert.Single(s.Items);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        Directory.Delete(FilePath);
        s.Add(P("b"));
        Assert.Null(s.LastSaveError);
        Assert.Single(messages);
        Assert.Equal(["a", "b"], Store().Items.Select(i => i.Title).OrderBy(x => x).ToArray());
        Assert.NotNull(a);
    }

    [Fact]
    public void Throwing_handlers_do_not_propagate()
    {
        var s = Store();
        var later = 0;
        s.Changed += (_, _) => throw new InvalidOperationException("boom");
        s.Changed += (_, _) => later++;
        s.SaveFailed += (_, _) => throw new InvalidOperationException("boom");
        Directory.CreateDirectory(FilePath);
        var item = s.Add(P("a"));
        Assert.NotNull(item);
        Assert.Equal(1, later);
    }

    [Fact]
    public void Reopen_removes_the_spawned_successor_and_repeats_leave_one_open_copy()
    {
        var s = Store();
        var a = s.Add(P("a every day"));
        for (var n = 0; n < 3; n++)
        {
            Assert.True(s.Complete(a.Id));
            Assert.Equal(2, s.Items.Count);
            var spawned = s.Items.Single(i => !i.IsDone);
            Assert.Equal(a.Id, spawned.SpawnedFromId);
            Assert.True(s.Reopen(a.Id));
            Assert.Single(s.Items);
            Assert.Equal(a.Id, s.Items.Single().Id);
            Assert.False(s.Items.Single().IsDone);
        }

        Assert.Single(Store().Items);
        Assert.True(s.Undo()); // reopen is undoable too
        Assert.Equal(2, s.Items.Count);
    }

    [Fact]
    public void Weekly_anchor_survives_postpone_and_spawn()
    {
        var s = Store(); // Saturday 10-10
        var a = s.Add(P("a every monday")); // due Mon 10-12
        Assert.Equal(DayOfWeek.Monday, a.RecurrenceDay);
        s.Postpone(a.Id); // Mon 12 -> Tue 13
        var moved = s.Items.Single();
        Assert.Equal(new DateOnly(2026, 10, 13), moved.Due);
        Assert.Equal(DayOfWeek.Monday, moved.RecurrenceDay);
        s.Complete(a.Id);
        var next = s.Items.Single(i => !i.IsDone);
        Assert.Equal(new DateOnly(2026, 10, 19), next.Due);
        Assert.Equal(DayOfWeek.Monday, next.RecurrenceDay);
        Assert.Equal(DayOfWeek.Monday, Store().Items.Single(i => !i.IsDone).RecurrenceDay);
    }

    [Fact]
    public void Weekly_anchor_defaults_to_due_weekday_and_is_cleared_for_other_recurrences()
    {
        var s = Store();
        var w = s.Add(P("a every week tomorrow")); // Sunday
        Assert.Equal(DayOfWeek.Sunday, w.RecurrenceDay);
        var d = s.Add(P("b every day"));
        Assert.Null(d.RecurrenceDay);
    }

    [Fact]
    public void Postpone_uses_max_of_due_and_today_plus_one()
    {
        var s = Store();
        var future = s.Add(P("a oct 20"));
        s.Postpone(future.Id);
        Assert.Equal(new DateOnly(2026, 10, 21), s.Items.Single().Due);
        var past = QuickAddParser.Parse("b", new DateOnly(2026, 10, 1))!;
        var b = s.Add(past);
        s.Postpone(b.Id);
        Assert.Equal(new DateOnly(2026, 10, 11), s.Items.Single(i => i.Id == b.Id).Due);
    }

    [Fact]
    public void Purge_clears_the_undo_snapshot_only_when_it_removes_something()
    {
        var s = Store();
        var a = s.Add(P("a"));
        _time.LocalNow = new DateTime(2026, 10, 1, 9, 0, 0);
        s.Complete(a.Id);
        _time.LocalNow = new DateTime(2026, 10, 10, 9, 0, 0);
        Assert.True(s.CanUndo);
        Assert.Equal(1, s.Purge());
        Assert.False(s.CanUndo);
        Assert.False(s.Undo());
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Purge_that_removes_nothing_keeps_undo()
    {
        var s = Store();
        s.Add(P("a"));
        Assert.Equal(0, s.Purge());
        Assert.True(s.CanUndo);
    }

    [Fact]
    public void Load_sanitizes_bad_items_and_tolerates_a_bad_time()
    {
        File.WriteAllText(FilePath, """
            {"version":1,"tasks":[
              {"id":"x","title":null,"due":"2026-10-11","time":"25:99","recurrence":"Daily"},
              {"id":"x","title":"  ","due":"2026-10-11","time":"09:30"},
              {"id":"","title":"done","due":"2026-10-05","completedAt":"2026-10-06T23:30:00+03:00","recurrence":99},
              {"id":"w","title":"weekly","due":"2026-10-12","recurrence":"Weekly","recurrenceDay":42},
              null
            ]}
            """);
        var items = Store().Items;
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Id)));
        Assert.Equal(4, items.Select(i => i.Id).Distinct().Count());
        Assert.Equal("(untitled)", items[0].Title);
        Assert.Null(items[0].Time);
        Assert.Equal(Recurrence.Daily, items[0].Recurrence);
        Assert.Equal("(untitled)", items[1].Title);
        Assert.Equal(new TimeOnly(9, 30), items[1].Time);
        Assert.Equal(new DateOnly(2026, 10, 6), items[2].CompletedOn);
        Assert.Equal(Recurrence.None, items[2].Recurrence);
        Assert.Null(items[3].RecurrenceDay);
        Assert.Empty(BadFiles());
    }
}
