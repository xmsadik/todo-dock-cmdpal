using TodoDock.Core;
using Xunit;

namespace TodoDock.Tests;

/// <summary>Regression tests for the Todo Dock core review findings (quick-add parser).</summary>
public class ReviewFixParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 10); // Saturday

    private static ParsedTask P(string text) => QuickAddParser.Parse(text, Today)!;

    [Theory]
    [InlineData("call ١٧:٠٠")] // Arabic-Indic digits
    [InlineData("call １７:００")] // fullwidth digits
    [InlineData("fatura ٣ gün sonra")]
    [InlineData("pay ١٢ oct")]
    public void Non_ascii_digits_are_never_tokens(string text)
    {
        var r = P(text);
        Assert.Equal(text, r.Title);
        Assert.Null(r.Time);
        Assert.Equal(Today, r.Due);
    }

    [Fact]
    public void Parse_never_throws_and_falls_back_to_a_plain_title_due_today()
    {
        // "tomorrow" overflows DateOnly.MaxValue inside the matcher.
        var r = QuickAddParser.Parse("  x tomorrow ", DateOnly.MaxValue);
        Assert.NotNull(r);
        Assert.Equal("x tomorrow", r!.Title);
        Assert.Equal(DateOnly.MaxValue, r.Due);
        Assert.Null(r.Time);
        Assert.Equal(Recurrence.None, r.Recurrence);
    }

    [Theory]
    [InlineData("Room 3", 0)]
    [InlineData("meeting at 17:00 prep", 2)]
    [InlineData("May day prep", 2)]
    [InlineData("sun cream", 0)]
    public void Edit_round_trip_preserves_titles_with_token_like_words(string title, int dayOffset)
    {
        foreach (var recurrence in new[] { Recurrence.None, Recurrence.Daily, Recurrence.Weekdays, Recurrence.Weekly })
        {
            var due = Today.AddDays(dayOffset + (recurrence == Recurrence.Weekdays ? 2 : 0));
            var item = new TodoItem
            {
                Title = title,
                Due = due,
                Time = new TimeOnly(9, 30),
                Recurrence = recurrence,
                RecurrenceDay = recurrence == Recurrence.Weekly ? due.DayOfWeek : null,
            };
            var text = QuickAddFormatter.ToText(item, Today);
            Assert.EndsWith(title, text);
            var again = P(text);
            Assert.Equal(title, again.Title);
            Assert.Equal(due, again.Due);
            Assert.Equal(new TimeOnly(9, 30), again.Time);
            Assert.Equal(recurrence, again.Recurrence);
            Assert.Equal(item.RecurrenceDay, again.RecurrenceDay);
        }
    }

    [Fact]
    public void Formatter_puts_tokens_first_and_title_last()
    {
        var item = new TodoItem { Title = "Room 3", Due = new DateOnly(2026, 10, 11), Time = new TimeOnly(9, 30), Recurrence = Recurrence.Daily };
        Assert.Equal("tomorrow 09:30 every day Room 3", QuickAddFormatter.ToText(item, Today));
        Assert.Equal("oct 12 09:30 every day Room 3", QuickAddFormatter.ToText(item with { Due = new DateOnly(2026, 10, 12) }, Today));
    }

    [Fact]
    public void Leading_tokens_win_and_the_rest_is_a_verbatim_title()
    {
        var r = P("tomorrow 09:30 meet   at home every day friday");
        Assert.Equal("meet   at home every day friday", r.Title);
        Assert.Equal(new DateOnly(2026, 10, 11), r.Due);
        Assert.Equal(new TimeOnly(9, 30), r.Time);
        Assert.Equal(Recurrence.None, r.Recurrence);
    }

    [Theory]
    [InlineData("gym daily")]
    [InlineData("weekly report")]
    [InlineData("sun cream")]
    [InlineData("the mon lisa")]
    [InlineData("buy 3.50 stamps")]
    [InlineData("room 2.10")]
    public void Common_words_and_decimals_stay_in_the_title(string text)
    {
        var r = P(text);
        Assert.Equal(text, r.Title);
        Assert.Equal(Recurrence.None, r.Recurrence);
        Assert.Null(r.Time);
        Assert.Equal(Today, r.Due);
    }

    [Theory]
    [InlineData("call 17.00", 17, 0)]
    [InlineData("call 10.10", 10, 10)]
    [InlineData("call 09.05", 9, 5)]
    public void Two_digit_hour_dotted_times_are_times(string text, int h, int m) =>
        Assert.Equal(new TimeOnly(h, m), P(text).Time);

    [Theory]
    [InlineData("pay 5 oct 2026", 2026, 10, 5)]
    [InlineData("pay oct 12 2027", 2027, 10, 12)]
    [InlineData("pay 10 ekim 2027", 2027, 10, 10)]
    [InlineData("pay oct 9 2026", 2026, 10, 9)] // explicit year: no next-year rollover
    public void Optional_year_is_consumed_and_used(string text, int y, int m, int d)
    {
        var r = P(text);
        Assert.Equal("pay", r.Title);
        Assert.Equal(new DateOnly(y, m, d), r.Due);
    }

    [Fact]
    public void Trailing_punctuation_is_ignored_for_matching_but_kept_in_the_title()
    {
        var r = P("buy milk, eggs tomorrow, 17:00!");
        Assert.Equal("buy milk, eggs", r.Title);
        Assert.Equal(new DateOnly(2026, 10, 11), r.Due);
        Assert.Equal(new TimeOnly(17, 0), r.Time);
        Assert.Equal("Hello, world", P("Hello, world today.").Title);
    }

    [Fact]
    public void Weekly_recurrence_day_comes_from_the_weekday_or_due()
    {
        Assert.Equal(DayOfWeek.Monday, P("x every monday").RecurrenceDay);
        Assert.Equal(DayOfWeek.Sunday, P("x every week tomorrow").RecurrenceDay);
        Assert.Null(P("x every day").RecurrenceDay);
    }
}
