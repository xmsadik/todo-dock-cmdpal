using TodoDock.Core;
using Xunit;

namespace TodoDock.Tests;

public class ParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 10); // Saturday

    private static ParsedTask P(string text) => QuickAddParser.Parse(text, Today)!;

    [Theory]
    [InlineData("pay bill today", 2026, 10, 10)]
    [InlineData("pay bill tomorrow", 2026, 10, 11)]
    [InlineData("pay bill tmr", 2026, 10, 11)]
    [InlineData("pay bill monday", 2026, 10, 12)]
    [InlineData("pay bill Friday", 2026, 10, 16)]
    [InlineData("pay bill in 3 days", 2026, 10, 13)]
    [InlineData("pay bill in 1 day", 2026, 10, 11)]
    [InlineData("pay bill next week", 2026, 10, 17)]
    [InlineData("pay bill oct 12", 2026, 10, 12)]
    [InlineData("pay bill 12 oct", 2026, 10, 12)]
    [InlineData("pay bill october 12", 2026, 10, 12)]
    [InlineData("pay bill dec 1", 2026, 12, 1)]
    [InlineData("pay bill january 5", 2027, 1, 5)]
    [InlineData("pay bill 2026-11-02", 2026, 11, 2)]
    public void English_dates(string text, int y, int m, int d)
    {
        var r = P(text);
        Assert.Equal("pay bill", r.Title);
        Assert.Equal(new DateOnly(y, m, d), r.Due);
    }

    [Theory]
    [InlineData("fatura bugün", 2026, 10, 10)]
    [InlineData("fatura YARIN", 2026, 10, 11)]
    [InlineData("fatura yarin", 2026, 10, 11)]
    [InlineData("fatura yarın", 2026, 10, 11)]
    [InlineData("fatura pazartesi", 2026, 10, 12)]
    [InlineData("fatura salı", 2026, 10, 13)]
    [InlineData("fatura çarşamba", 2026, 10, 14)]
    [InlineData("fatura perşembe", 2026, 10, 15)]
    [InlineData("fatura cuma", 2026, 10, 16)]
    [InlineData("fatura cumartesi", 2026, 10, 17)]
    [InlineData("fatura pazar", 2026, 10, 11)]
    [InlineData("fatura 3 gün sonra", 2026, 10, 13)]
    [InlineData("fatura haftaya", 2026, 10, 17)]
    [InlineData("fatura 12 ekim", 2026, 10, 12)]
    [InlineData("fatura 5 şubat", 2027, 2, 5)]
    [InlineData("fatura 1 aralık", 2026, 12, 1)]
    public void Turkish_dates(string text, int y, int m, int d)
    {
        var r = P(text);
        Assert.Equal("fatura", r.Title);
        Assert.Equal(new DateOnly(y, m, d), r.Due);
    }

    [Theory]
    [InlineData("call 17:00", 17, 0)]
    [InlineData("call 9:30", 9, 30)]
    [InlineData("call 17.00", 17, 0)]
    [InlineData("call 5pm", 17, 0)]
    [InlineData("call 5:30pm", 17, 30)]
    [InlineData("call 5 pm", 17, 0)]
    [InlineData("call at 5pm", 17, 0)]
    [InlineData("call 12am", 0, 0)]
    [InlineData("call saat 17", 17, 0)]
    [InlineData("call saat 17:30", 17, 30)]
    public void Times(string text, int h, int m)
    {
        var r = P(text);
        Assert.Equal("call", r.Title);
        Assert.Equal(new TimeOnly(h, m), r.Time);
    }

    [Fact]
    public void At_is_kept_when_not_followed_by_a_time() =>
        Assert.Equal("meet at home", P("meet at home").Title);

    [Fact]
    public void Invalid_time_is_not_a_token()
    {
        var r = P("call 25:00");
        Assert.Null(r.Time);
        Assert.Equal("call 25:00", r.Title);
    }

    [Fact]
    public void Ten_dot_ten_is_a_time_not_a_date()
    {
        var r = P("call 10.10");
        Assert.Equal(new TimeOnly(10, 10), r.Time);
        Assert.Equal(Today, r.Due);
    }

    [Fact]
    public void Bare_numbers_stay_in_title() => Assert.Equal("buy 3 apples", P("buy 3 apples").Title);

    [Theory]
    [InlineData("gym every day", Recurrence.Daily)]
    [InlineData("gym weekdays", Recurrence.Weekdays)]
    [InlineData("gym every weekday", Recurrence.Weekdays)]
    [InlineData("gym every week", Recurrence.Weekly)]
    [InlineData("gym every monday", Recurrence.Weekly)]
    [InlineData("spor her gün", Recurrence.Daily)]
    [InlineData("spor hafta içi", Recurrence.Weekdays)]
    [InlineData("spor hafta içi her gün", Recurrence.Weekdays)]
    [InlineData("spor her hafta", Recurrence.Weekly)]
    [InlineData("spor her pazartesi", Recurrence.Weekly)]
    public void Recurrence_tokens(string text, Recurrence expected)
    {
        var r = P(text);
        Assert.Equal(expected, r.Recurrence);
        Assert.True(r.Title is "gym" or "spor");
    }

    [Fact]
    public void Every_weekday_name_counts_today()
    {
        Assert.Equal(Today, P("gym every saturday").Due);
        Assert.Equal(new DateOnly(2026, 10, 12), P("spor her pazartesi").Due);
    }

    [Fact]
    public void Weekday_equal_to_today_means_next_week()
    {
        Assert.Equal(new DateOnly(2026, 10, 17), P("x saturday").Due);
        Assert.Equal(new DateOnly(2026, 10, 17), P("x cumartesi").Due);
    }

    [Fact]
    public void Weekdays_recurrence_without_date_skips_weekend()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), P("x weekdays").Due); // Saturday -> Monday
        Assert.Equal(new DateOnly(2026, 10, 14), QuickAddParser.Parse("x weekdays", new DateOnly(2026, 10, 14))!.Due);
    }

    [Fact]
    public void Date_without_year_rolls_to_next_year()
    {
        Assert.Equal(new DateOnly(2027, 10, 9), P("x oct 9").Due);
        Assert.Equal(Today, P("x oct 10").Due);
    }

    [Fact]
    public void Tokens_can_appear_anywhere_and_title_is_rejoined()
    {
        var r = P("pay   the bill tomorrow 09:30 every day");
        Assert.Equal("pay the bill", r.Title);
        Assert.Equal(new DateOnly(2026, 10, 11), r.Due);
        Assert.Equal(new TimeOnly(9, 30), r.Time);
        Assert.Equal(Recurrence.Daily, r.Recurrence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tomorrow")]
    [InlineData("tomorrow 17:00 every day")]
    public void Empty_title_returns_null(string text) => Assert.Null(QuickAddParser.Parse(text, Today));

    [Fact]
    public void No_date_defaults_to_today() => Assert.Equal(Today, P("hello").Due);

    [Fact]
    public void Descriptions()
    {
        Assert.Equal("Tomorrow · 09:30 · every day", P("x tomorrow 9:30 every day").Description);
        Assert.Equal("Today", P("x").Description);
        Assert.Equal("Fri, Oct 16", P("x friday").Description);
    }

    [Theory]
    [InlineData("Pay bill oct 11 09:30 every day")]
    [InlineData("Pay bill tomorrow")]
    [InlineData("Pay bill today 17:00 every weekday")]
    [InlineData("Pay bill dec 24 every week")]
    public void Formatter_round_trips(string text)
    {
        var p = P(text);
        var item = new TodoItem { Title = p.Title, Due = p.Due, Time = p.Time, Recurrence = p.Recurrence };
        var again = P(QuickAddFormatter.ToText(item, Today));
        Assert.Equal(p with { Description = "" }, again with { Description = "" });
    }

    [Fact]
    public void Formatter_canonical_text_and_overdue_and_far_dates()
    {
        var item = new TodoItem { Title = "Pay bill", Due = new DateOnly(2026, 10, 11), Time = new TimeOnly(9, 30), Recurrence = Recurrence.Daily };
        Assert.Equal("tomorrow 09:30 every day Pay bill", QuickAddFormatter.ToText(item, Today));
        item = item with { Due = new DateOnly(2026, 10, 12) };
        Assert.Equal("oct 12 09:30 every day Pay bill", QuickAddFormatter.ToText(item, Today));

        foreach (var due in new[] { new DateOnly(2026, 10, 5), new DateOnly(2027, 12, 1) })
        {
            var i = new TodoItem { Title = "T", Due = due };
            Assert.Equal(due, P(QuickAddFormatter.ToText(i, Today)).Due);
        }
    }
}
