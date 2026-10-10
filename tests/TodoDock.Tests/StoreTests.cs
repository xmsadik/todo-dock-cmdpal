using TodoDock.Core;
using Xunit;

namespace TodoDock.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TodoDockTests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTime(2026, 10, 10, 14, 0, 0)); // Saturday
    private string FilePath => Path.Combine(_dir, "sub", "tasks.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private TaskStore Store() => new(FilePath, _time);

    private static ParsedTask P(string text, DateOnly today) => QuickAddParser.Parse(text, today)!;

    private ParsedTask P(string text) => P(text, _time.Today);

    [Fact]
    public void Missing_file_is_empty_and_save_creates_directory_and_round_trips()
    {
        var s = Store();
        Assert.Empty(s.Items);
        s.Add(P("Pay bill tomorrow 09:30 every day"));
        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"due\": \"2026-10-11\"", json);
        Assert.Contains("\"time\": \"09:30\"", json);
        Assert.Contains("\"recurrence\": \"Daily\"", json);

        var reloaded = Store().Items.Single();
        Assert.Equal("Pay bill", reloaded.Title);
        Assert.Equal(new DateOnly(2026, 10, 11), reloaded.Due);
        Assert.Equal(new TimeOnly(9, 30), reloaded.Time);
        Assert.Equal(Recurrence.Daily, reloaded.Recurrence);
        Assert.Equal(s.Items.Single(), reloaded);
    }

    [Fact]
    public void Corrupt_file_is_renamed_and_store_starts_empty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        var s = Store();
        Assert.Empty(s.Items);
        Assert.False(File.Exists(FilePath));
        var bad = Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "tasks.json.bad-20261010140000000");
        Assert.Single(bad);
        s.Add(P("works"));
        Assert.Single(Store().Items);
    }

    [Fact]
    public void Changed_event_fires_and_items_is_a_snapshot()
    {
        var s = Store();
        var count = 0;
        s.Changed += (_, _) => count++;
        var snapshot = s.Items;
        var item = s.Add(P("a"));
        s.Complete(item.Id);
        s.Delete(item.Id);
        Assert.Equal(3, count);
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Complete_non_recurring_and_reopen()
    {
        var s = Store();
        var a = s.Add(P("a"));
        Assert.True(s.Complete(a.Id));
        var done = s.Items.Single();
        Assert.Equal(new DateOnly(2026, 10, 10), done.CompletedOn);
        Assert.NotNull(done.CompletedAt);
        Assert.True(s.Reopen(a.Id));
        Assert.Null(s.Items.Single().CompletedAt);
        Assert.Null(s.Items.Single().CompletedOn);
    }

    [Fact]
    public void Daily_three_days_overdue_spawns_tomorrow()
    {
        var s = Store();
        var a = s.Add(P("a every day", new DateOnly(2026, 10, 7)));
        s.Complete(a.Id);
        var next = s.Items.Single(i => !i.IsDone);
        Assert.Equal(new DateOnly(2026, 10, 11), next.Due);
        Assert.Equal("a", next.Title);
        Assert.Equal(Recurrence.Daily, next.Recurrence);
    }

    [Fact]
    public void Daily_due_in_future_spawns_day_after_due()
    {
        var s = Store();
        var a = s.Add(P("a every day tomorrow"));
        s.Complete(a.Id);
        Assert.Equal(new DateOnly(2026, 10, 12), s.Items.Single(i => !i.IsDone).Due);
    }

    [Fact]
    public void Weekdays_over_weekend_spawns_monday_and_friday_spawns_monday()
    {
        var s = Store(); // today is Saturday
        var a = s.Add(P("a weekdays"));
        Assert.Equal(new DateOnly(2026, 10, 12), a.Due); // Monday
        s.Complete(a.Id);
        Assert.Equal(new DateOnly(2026, 10, 13), s.Items.Single(i => !i.IsDone).Due);

        _time.LocalNow = new DateTime(2026, 10, 16, 10, 0, 0); // Friday
        var b = s.Add(P("b weekdays"));
        s.Complete(b.Id);
        Assert.Equal(new DateOnly(2026, 10, 19), s.Items.Single(i => i.Title == "b" && !i.IsDone).Due);
    }

    [Fact]
    public void Weekly_keeps_same_day_of_week()
    {
        var s = Store();
        var a = s.Add(P("a every monday")); // due Mon 10-12
        _time.LocalNow = new DateTime(2026, 10, 20, 9, 0, 0); // Tuesday, a is 8 days overdue
        s.Complete(a.Id);
        Assert.Equal(new DateOnly(2026, 10, 26), s.Items.Single(i => !i.IsDone).Due); // next Monday
    }

    [Fact]
    public void Undo_of_recurring_completion_removes_spawned_item()
    {
        var s = Store();
        var a = s.Add(P("a every day"));
        var before = s.Items;
        s.Complete(a.Id);
        Assert.Equal(2, s.Items.Count);
        Assert.True(s.CanUndo);
        Assert.True(s.Undo());
        Assert.Equal(before, s.Items);
        Assert.False(s.CanUndo);
        Assert.False(s.Undo());
        Assert.Equal(before, Store().Items);
    }

    [Fact]
    public void Undo_reverts_delete_update_add_and_postpone()
    {
        var s = Store();
        var a = s.Add(P("a"));
        var original = s.Items;

        s.Delete(a.Id);
        s.Undo();
        Assert.Equal(original, s.Items);

        s.Update(a.Id, P("renamed tomorrow 10:00"));
        Assert.Equal("renamed", s.Items.Single().Title);
        s.Undo();
        Assert.Equal(original, s.Items);

        s.Postpone(a.Id);
        s.Undo();
        Assert.Equal(original, s.Items);

        s.Add(P("b"));
        s.Undo();
        Assert.Equal(original, s.Items);
    }

    [Fact]
    public void Postpone_moves_to_tomorrow_keeps_time_and_only_this_instance()
    {
        var s = Store();
        var a = s.Add(P("a 09:00 every day", new DateOnly(2026, 10, 5)));
        s.Postpone(a.Id);
        var moved = s.Items.Single();
        Assert.Equal(new DateOnly(2026, 10, 11), moved.Due);
        Assert.Equal(new TimeOnly(9, 0), moved.Time);
        Assert.Equal(Recurrence.Daily, moved.Recurrence);
        Assert.Single(s.Items);
    }

    [Fact]
    public void Purge_removes_old_completions_but_never_todays()
    {
        var s = Store();
        var old = s.Add(P("old"));
        var edge = s.Add(P("edge"));
        var today = s.Add(P("fresh"));
        _time.LocalNow = new DateTime(2026, 10, 1, 9, 0, 0);
        s.Complete(old.Id); // completed 10-01 (9 days before 10-10)
        _time.LocalNow = new DateTime(2026, 10, 3, 9, 0, 0);
        s.Complete(edge.Id); // 10-03 = exactly 7 days before 10-10: kept
        _time.LocalNow = new DateTime(2026, 10, 10, 9, 0, 0);
        s.Complete(today.Id);

        Assert.Equal(1, s.Purge());
        Assert.Equal(["edge", "fresh"], s.Items.Select(i => i.Title).OrderBy(x => x).ToArray());

        _time.LocalNow = new DateTime(2026, 10, 11, 9, 0, 0);
        Assert.Equal(1, s.Purge());
        Assert.Equal("fresh", s.Items.Single().Title);
        _time.LocalNow = new DateTime(2026, 10, 17, 9, 0, 0);
        Assert.Equal(0, s.Purge()); // 10-10 == today - 7: kept
        _time.LocalNow = new DateTime(2026, 10, 18, 9, 0, 0);
        Assert.Equal(1, s.Purge());
    }

    [Fact]
    public void Purge_runs_on_load()
    {
        var s = Store();
        var a = s.Add(P("a"));
        _time.LocalNow = new DateTime(2026, 10, 1, 9, 0, 0);
        s.Complete(a.Id);
        _time.LocalNow = new DateTime(2026, 10, 10, 9, 0, 0);
        Assert.Empty(Store().Items);
    }

    [Fact]
    public void Unknown_id_operations_return_false()
    {
        var s = Store();
        Assert.False(s.Complete("x"));
        Assert.False(s.Delete("x"));
        Assert.False(s.Postpone("x"));
        Assert.False(s.Reopen("x"));
        Assert.False(s.Update("x", P("a")));
    }

    [Fact]
    public void UndoDescription_names_the_last_action_and_clears()
    {
        var s = Store();
        Assert.Null(s.UndoDescription);
        var a = s.Add(P("pay bill"));
        Assert.Equal("Added 'pay bill'", s.UndoDescription);
        s.Postpone(a.Id);
        Assert.Equal("Postponed 'pay bill'", s.UndoDescription);
        s.Update(a.Id, P("rent"));
        Assert.Equal("Edited 'pay bill'", s.UndoDescription);
        s.Complete(a.Id);
        Assert.Equal("Completed 'rent'", s.UndoDescription);
        s.Reopen(a.Id);
        Assert.Equal("Reopened 'rent'", s.UndoDescription);
        s.Delete(a.Id);
        Assert.Equal("Deleted 'rent'", s.UndoDescription);
        s.Undo();
        Assert.Null(s.UndoDescription);
    }
}
