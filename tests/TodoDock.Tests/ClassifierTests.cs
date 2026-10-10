using TodoDock.Core;
using Xunit;

namespace TodoDock.Tests;

public class ClassifierTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 14, 0, 0);
    private static readonly DateOnly Today = new(2026, 10, 10);
    private static int _n;

    private static TodoItem T(string title, DateOnly due, TimeOnly? time = null, DateOnly? doneOn = null) => new()
    {
        Id = title,
        Title = title,
        Due = due,
        Time = time,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(_n++),
        CompletedAt = doneOn is null ? null : new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero).AddSeconds(_n++),
        CompletedOn = doneOn,
    };

    private static string[] Titles(IEnumerable<TodoItem> items) => items.Select(i => i.Title).ToArray();

    [Fact]
    public void Past_date_is_overdue_and_date_only_today_is_not()
    {
        var s = TaskClassifier.Classify([T("old", Today.AddDays(-1)), T("dateonly", Today)], Now);
        Assert.Equal(["old"], Titles(s.Overdue));
        Assert.Equal(["dateonly"], Titles(s.Today));
    }

    [Fact]
    public void Timed_task_becomes_overdue_when_time_passes()
    {
        var items = new[] { T("t", Today, new TimeOnly(14, 0)) };
        Assert.Empty(TaskClassifier.Classify(items, Now).Overdue); // equal is not yet overdue
        Assert.Single(TaskClassifier.Classify(items, Now.AddMinutes(1)).Overdue);
        Assert.Single(TaskClassifier.Classify(items, Now.AddHours(-1)).Today);
    }

    [Fact]
    public void Date_only_task_overdue_at_midnight()
    {
        var items = new[] { T("d", Today) };
        Assert.Empty(TaskClassifier.Classify(items, new DateTime(2026, 10, 10, 23, 59, 59)).Overdue);
        Assert.Single(TaskClassifier.Classify(items, new DateTime(2026, 10, 11, 0, 0, 0)).Overdue);
    }

    [Fact]
    public void Sorting_rules()
    {
        var s = TaskClassifier.Classify(
        [
            T("untimed1", Today),
            T("late", Today, new TimeOnly(18, 0)),
            T("early", Today, new TimeOnly(15, 0)),
            T("untimed2", Today),
            T("up2", Today.AddDays(2)),
            T("up1b", Today.AddDays(1), new TimeOnly(10, 0)),
            T("up1a", Today.AddDays(1), new TimeOnly(8, 0)),
            T("od2", Today.AddDays(-1)),
            T("od1", Today.AddDays(-3)),
            T("od0", Today, new TimeOnly(9, 0)),
        ], Now);
        Assert.Equal(["early", "late", "untimed1", "untimed2"], Titles(s.Today));
        Assert.Equal(["up1a", "up1b", "up2"], Titles(s.Upcoming));
        Assert.Equal(["od1", "od2", "od0"], Titles(s.Overdue));
    }

    [Fact]
    public void Done_is_newest_first()
    {
        var a = T("a", Today, doneOn: Today);
        var b = T("b", Today, doneOn: Today);
        Assert.Equal(["b", "a"], Titles(TaskClassifier.Classify([a, b], Now).Done));
    }

    [Fact]
    public void Progress_today_counts_overdue_finished_today()
    {
        var s = TaskClassifier.Classify(
        [
            T("oldDoneToday", Today.AddDays(-5), doneOn: Today),
            T("doneYesterday", Today.AddDays(-1), doneOn: Today.AddDays(-1)),
            T("openToday", Today),
            T("overdue", Today.AddDays(-2)),
            T("future", Today.AddDays(3)),
        ], Now);
        Assert.Equal(1, s.ProgressDone);
        Assert.Equal(3, s.ProgressTotal);
        Assert.Equal(33, s.Percent);
    }

    [Fact]
    public void Progress_all_mode()
    {
        var s = TaskClassifier.Classify([T("a", Today, doneOn: Today.AddDays(-2)), T("b", Today), T("c", Today.AddDays(4))], Now, "all");
        Assert.Equal(1, s.ProgressDone);
        Assert.Equal(3, s.ProgressTotal);
    }

    [Fact]
    public void Percent_rounds_half_away_from_zero_and_labels()
    {
        var items = new List<TodoItem>();
        for (var i = 0; i < 3; i++)
        {
            items.Add(T("d" + i, Today, doneOn: Today));
        }

        for (var i = 0; i < 4; i++)
        {
            items.Add(T("o" + i, Today));
        }

        var s = TaskClassifier.Classify(items, Now);
        Assert.Equal(43, s.Percent);
        Assert.Equal("3/7 · 43%", s.Label("progress"));
        Assert.Equal("43%", s.Label("percent"));
        Assert.Equal("", s.Label("icon"));

        var half = TaskClassifier.Classify([T("a", Today, doneOn: Today), T("b", Today), T("c", Today), T("d", Today), T("e", Today), T("f", Today), T("g", Today), T("h", Today)], Now);
        Assert.Equal(13, half.Percent); // 12.5 -> 13
    }

    [Fact]
    public void Empty_state_label_is_empty()
    {
        var s = TaskClassifier.Classify([T("future", Today.AddDays(2))], Now);
        Assert.Null(s.Percent);
        Assert.Equal("", s.Label("progress"));
        Assert.Equal("", s.Label("percent"));
        Assert.Equal(Light.None, s.Light);
        Assert.Equal("No tasks for today", s.Tooltip);
    }

    [Fact]
    public void Light_precedence()
    {
        Assert.Equal(Light.Red, TaskClassifier.Classify([T("o", Today.AddDays(-1)), T("t", Today)], Now).Light);
        Assert.Equal(Light.Orange, TaskClassifier.Classify([T("t", Today), T("d", Today, doneOn: Today)], Now).Light);
        Assert.Equal(Light.Green, TaskClassifier.Classify([T("d", Today, doneOn: Today)], Now).Light);
        Assert.Equal(Light.None, TaskClassifier.Classify([], Now).Light);
        Assert.Equal("🔴", LightIcons.Emoji(Light.Red));
        Assert.Equal("🟠", LightIcons.Emoji(Light.Orange));
        Assert.Equal("🟢", LightIcons.Emoji(Light.Green));
        Assert.Equal("⚪", LightIcons.Emoji(Light.None));
    }

    [Fact]
    public void Tooltips()
    {
        var one = TaskClassifier.Classify([T("A", Today.AddDays(-1))], Now);
        Assert.Equal("Overdue: A", one.Tooltip);
        var many = TaskClassifier.Classify([T("A", Today.AddDays(-2)), T("B", Today.AddDays(-1)), T("C", Today.AddDays(-1))], Now);
        Assert.Equal("Overdue: A (+2 more)", many.Tooltip);
        Assert.Equal("Next: X at 15:30", TaskClassifier.Classify([T("X", Today, new TimeOnly(15, 30))], Now).Tooltip);
        Assert.Equal("Next: Y", TaskClassifier.Classify([T("Y", Today)], Now).Tooltip);
        Assert.Equal("All done for today", TaskClassifier.Classify([T("Z", Today, doneOn: Today)], Now).Tooltip);
        var counts = TaskClassifier.Classify([T("a", Today.AddDays(-1)), T("b", Today.AddDays(-1)), T("c", Today), T("d", Today.AddDays(2))], Now);
        Assert.Equal("2 overdue · 1 today · 1 upcoming", counts.CountsLine);
    }
}
