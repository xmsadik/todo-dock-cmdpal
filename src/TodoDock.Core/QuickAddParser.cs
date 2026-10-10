using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TodoDock.Core;

/// <summary>Result of parsing quick-add text. <see cref="Description"/> is the English preview ("Tomorrow · 09:30").</summary>
public sealed record ParsedTask(
    string Title, DateOnly Due, TimeOnly? Time, Recurrence Recurrence, string Description, DayOfWeek? RecurrenceDay = null);

/// <summary>Parses free text such as "pay bill tomorrow 17:00 every day" (English and Turkish tokens).</summary>
public static partial class QuickAddParser
{
    private static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["monday"] = DayOfWeek.Monday, ["pazartesi"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday, ["sali"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["carsamba"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday, ["persembe"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday, ["cuma"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday, ["cumartesi"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday, ["pazar"] = DayOfWeek.Sunday,
    };

    private static readonly Dictionary<string, int> Months = new()
    {
        ["january"] = 1, ["jan"] = 1, ["ocak"] = 1,
        ["february"] = 2, ["feb"] = 2, ["subat"] = 2,
        ["march"] = 3, ["mar"] = 3, ["mart"] = 3,
        ["april"] = 4, ["apr"] = 4, ["nisan"] = 4,
        ["may"] = 5, ["mayis"] = 5,
        ["june"] = 6, ["jun"] = 6, ["haziran"] = 6,
        ["july"] = 7, ["jul"] = 7, ["temmuz"] = 7,
        ["august"] = 8, ["aug"] = 8, ["agustos"] = 8,
        ["september"] = 9, ["sep"] = 9, ["eylul"] = 9,
        ["october"] = 10, ["oct"] = 10, ["ekim"] = 10,
        ["november"] = 11, ["nov"] = 11, ["kasim"] = 11,
        ["december"] = 12, ["dec"] = 12, ["aralik"] = 12,
    };

    private static readonly char[] TrailingPunctuation = [',', '.', ';', '!', '?'];

    public static ParsedTask? Parse(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ParseCore(text, today);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"quick-add parse failed: {ex.Message}");
            return new ParsedTask(text.Trim(), today, null, Recurrence.None, "Today");
        }
    }

    private static ParsedTask? ParseCore(string text, DateOnly today)
    {
        var matches = WordPattern().Matches(text);
        var words = new string[matches.Count];
        for (var k = 0; k < words.Length; k++)
        {
            words[k] = matches[k].Value;
        }

        var norm = words.Select(w => Normalize(w.TrimEnd(TrailingPunctuation))).ToArray();
        var used = new bool[words.Length];

        DateOnly? date = null;
        DayOfWeek? weekday = null;
        DayOfWeek? recurDay = null;
        TimeOnly? time = null;
        var recurrence = Recurrence.None;

        // A text that starts with tokens is "<tokens> <title>": only the leading run is parsed and the
        // rest stays verbatim. Otherwise tokens may appear anywhere in the text.
        Scan(leadingOnly: true);
        var leading = used[0];
        if (!leading)
        {
            Scan(leadingOnly: false);
        }

        string title;
        if (leading)
        {
            var firstTitle = Array.IndexOf(used, false);
            title = firstTitle < 0 ? "" : text[matches[firstTitle].Index..].Trim();
        }
        else
        {
            title = string.Join(' ', words.Where((_, idx) => !used[idx]));
        }

        if (title.Length == 0)
        {
            return null;
        }

        DateOnly due;
        if (date is { } explicitDate)
        {
            due = explicitDate;
        }
        else if (recurDay is { } rd)
        {
            due = today.AddDays(((int)rd - (int)today.DayOfWeek + 7) % 7);
        }
        else if (weekday is { } w)
        {
            var offset = ((int)w - (int)today.DayOfWeek + 7) % 7;
            due = today.AddDays(offset == 0 ? 7 : offset);
        }
        else if (recurrence == Recurrence.Weekdays)
        {
            due = today;
            while (due.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                due = due.AddDays(1);
            }
        }
        else
        {
            due = today;
        }

        DayOfWeek? anchor = recurrence == Recurrence.Weekly ? recurDay ?? due.DayOfWeek : null;
        return new ParsedTask(title, due, time, recurrence, Describe(due, time, recurrence, today), anchor);

        void Scan(bool leadingOnly)
        {
            for (var i = 0; i < words.Length; i++)
            {
                var n = MatchRecurrence(norm, i, out var rec, out var rDay);
                if (n > 0)
                {
                    if (recurrence == Recurrence.None)
                    {
                        recurrence = rec;
                        recurDay = rDay;
                    }

                    Consume(used, i, n);
                    i += n - 1;
                    continue;
                }

                n = MatchDate(norm, i, today, out var d, out var wd);
                if (n > 0)
                {
                    if (d is not null)
                    {
                        date ??= d;
                    }
                    else if (wd is not null)
                    {
                        weekday ??= wd;
                    }

                    Consume(used, i, n);
                    i += n - 1;
                    continue;
                }

                n = MatchTime(norm, i, out var t);
                if (n > 0)
                {
                    time ??= t;
                    Consume(used, i, n);
                    i += n - 1;
                    continue;
                }

                if (leadingOnly)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Lowercase invariant and fold Turkish letters to ASCII (matching only).</summary>
    internal static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var raw in word)
        {
            var c = raw switch
            {
                'İ' or 'I' or 'ı' => 'i',
                _ => char.ToLowerInvariant(raw),
            };
            sb.Append(c switch
            {
                'ş' => 's',
                'ğ' => 'g',
                'ü' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                _ => c,
            });
        }

        return sb.ToString();
    }

    internal static string Describe(DateOnly due, TimeOnly? time, Recurrence recurrence, DateOnly today)
    {
        var parts = new List<string>
        {
            due == today ? "Today"
                : due == today.AddDays(1) ? "Tomorrow"
                : due.ToString("ddd, MMM d", CultureInfo.InvariantCulture),
        };
        if (time is { } t)
        {
            parts.Add(t.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        switch (recurrence)
        {
            case Recurrence.Daily: parts.Add("every day"); break;
            case Recurrence.Weekdays: parts.Add("every weekday"); break;
            case Recurrence.Weekly: parts.Add("every week"); break;
        }

        return string.Join(" · ", parts);
    }

    private static void Consume(bool[] used, int start, int count)
    {
        for (var k = 0; k < count; k++)
        {
            used[start + k] = true;
        }
    }

    private static bool Is(string[] w, int i, string value) => i < w.Length && w[i] == value;

    private static int MatchRecurrence(string[] w, int i, out Recurrence rec, out DayOfWeek? day)
    {
        rec = Recurrence.None;
        day = null;
        var s = w[i];
        if (s is "weekdays")
        {
            rec = Recurrence.Weekdays;
            return 1;
        }

        if (s == "every" && i + 1 < w.Length)
        {
            var next = w[i + 1];
            if (next == "day")
            {
                rec = Recurrence.Daily;
                return 2;
            }

            if (next == "weekday")
            {
                rec = Recurrence.Weekdays;
                return 2;
            }

            if (next == "week")
            {
                rec = Recurrence.Weekly;
                return 2;
            }

            if (Weekdays.TryGetValue(next, out var d))
            {
                rec = Recurrence.Weekly;
                day = d;
                return 2;
            }
        }

        if (s == "her" && i + 1 < w.Length)
        {
            var next = w[i + 1];
            if (next == "gun")
            {
                rec = Recurrence.Daily;
                return 2;
            }

            if (next == "hafta")
            {
                rec = Recurrence.Weekly;
                return 2;
            }

            if (Weekdays.TryGetValue(next, out var d))
            {
                rec = Recurrence.Weekly;
                day = d;
                return 2;
            }
        }

        if (s == "hafta" && Is(w, i + 1, "ici"))
        {
            rec = Recurrence.Weekdays;
            return Is(w, i + 2, "her") && Is(w, i + 3, "gun") ? 4 : 2;
        }

        return 0;
    }

    /// <summary>Matches a date token. Sets either a concrete date or a one-off weekday.</summary>
    private static int MatchDate(string[] w, int i, DateOnly today, out DateOnly? date, out DayOfWeek? weekday)
    {
        date = null;
        weekday = null;
        var s = w[i];

        switch (s)
        {
            case "today" or "bugun":
                date = today;
                return 1;
            case "tomorrow" or "tmr" or "yarin":
                date = today.AddDays(1);
                return 1;
            case "haftaya":
                date = today.AddDays(7);
                return 1;
            case "next" when Is(w, i + 1, "week"):
                date = today.AddDays(7);
                return 2;
        }

        if (Weekdays.TryGetValue(s, out var wd))
        {
            weekday = wd;
            return 1;
        }

        if (s == "in" && i + 2 < w.Length && TryInt(w[i + 1], out var inDays) && w[i + 2] is "day" or "days")
        {
            date = today.AddDays(inDays);
            return 3;
        }

        if (TryInt(s, out var num))
        {
            if (i + 2 < w.Length && w[i + 1] == "gun" && w[i + 2] == "sonra")
            {
                date = today.AddDays(num);
                return 3;
            }

            if (i + 1 < w.Length && Months.TryGetValue(w[i + 1], out var m1))
            {
                if (i + 2 < w.Length && TryYear(w[i + 2], out var y1))
                {
                    if (WithYear(y1, m1, num) is { } dy1)
                    {
                        date = dy1;
                        return 3;
                    }
                }
                else if (ResolveMonthDay(m1, num, today) is { } d1)
                {
                    date = d1;
                    return 2;
                }
            }
        }

        if (Months.TryGetValue(s, out var m2) && i + 1 < w.Length && TryInt(w[i + 1], out var day2))
        {
            if (i + 2 < w.Length && TryYear(w[i + 2], out var y2))
            {
                if (WithYear(y2, m2, day2) is { } dy2)
                {
                    date = dy2;
                    return 3;
                }
            }
            else if (ResolveMonthDay(m2, day2, today) is { } d2)
            {
                date = d2;
                return 2;
            }
        }

        if (IsoDate().IsMatch(s) && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            date = iso;
            return 1;
        }

        return 0;
    }

    /// <summary>Explicit-year date (no rollover); null when the day does not exist in that month.</summary>
    private static DateOnly? WithYear(int year, int month, int day) =>
        day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

    private static DateOnly? ResolveMonthDay(int month, int day, DateOnly today)
    {
        if (day is < 1 or > 31)
        {
            return null;
        }

        for (var y = today.Year; y <= today.Year + 8; y++)
        {
            if (day <= DateTime.DaysInMonth(y, month))
            {
                var candidate = new DateOnly(y, month, day);
                if (candidate >= today)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static int MatchTime(string[] w, int i, out TimeOnly time)
    {
        time = default;
        var s = w[i];
        if (s == "at" && i + 1 < w.Length)
        {
            var c = ParseTime(w, i + 1, out time);
            return c > 0 ? c + 1 : 0;
        }

        if (s == "saat" && i + 1 < w.Length)
        {
            var c = ParseTime(w, i + 1, out time);
            if (c > 0)
            {
                return c + 1;
            }

            if (TryInt(w[i + 1], out var h) && h is >= 0 and <= 23)
            {
                time = new TimeOnly(h, 0);
                return 2;
            }

            return 0;
        }

        return ParseTime(w, i, out time);
    }

    /// <summary>Parses "17:00", "17.00", "5pm", "5:30pm", "5 pm" at index i. Returns words consumed (0 = none).</summary>
    private static int ParseTime(string[] w, int i, out TimeOnly time)
    {
        time = default;
        var m = TimePattern().Match(w[i]);
        if (!m.Success)
        {
            return 0;
        }

        var hour = int.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : 0;
        var suffix = m.Groups[3].Success ? m.Groups[3].Value : null;
        var consumed = 1;
        if (suffix is null && i + 1 < w.Length && w[i + 1] is "am" or "pm")
        {
            suffix = w[i + 1];
            consumed = 2;
        }

        if (minute > 59)
        {
            return 0;
        }

        if (suffix is not null)
        {
            if (hour is < 1 or > 12)
            {
                return 0;
            }

            hour = hour % 12 + (suffix == "pm" ? 12 : 0);
        }
        else
        {
            // A bare number is not a time; it needs ':' or '.' minutes.
            if (!m.Groups[2].Success || hour > 23)
            {
                return 0;
            }
        }

        time = new TimeOnly(hour, minute);
        return consumed;
    }

    private static bool TryYear(string s, out int year)
    {
        year = 0;
        return s.Length == 4 && s.All(char.IsAsciiDigit)
            && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out year) && year is >= 2000 and <= 2999;
    }

    private static bool TryInt(string s, out int value)
    {
        value = 0;
        return s.Length is >= 1 and <= 3 && s.All(char.IsAsciiDigit)
            && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // Colon times: H:MM / HH:MM. Dotted times only as two-digit hour HH.MM ("17.00" yes, "3.50" no).
    [GeneratedRegex(@"^([0-9]{1,2})(?:(?::|(?<=^[0-9]{2})\.)([0-9]{2}))?(am|pm)?$")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordPattern();
}

/// <summary>Renders a task back to canonical quick-add text (round-trips through <see cref="QuickAddParser"/>).</summary>
public static class QuickAddFormatter
{
    public static string ToText(TodoItem item, DateOnly today)
    {
        // Tokens first, title last: the parser then reads only the leading run, so a title that
        // happens to contain date/time words survives an edit round-trip untouched.
        var parts = new List<string>();
        if (item.Due == today)
        {
            parts.Add("today");
        }
        else if (item.Due == today.AddDays(1))
        {
            parts.Add("tomorrow");
        }
        else if (item.Due > today && item.Due.AddYears(-1) < today)
        {
            // Year-less form resolves to the first occurrence on/after today, which is Due itself.
            parts.Add(item.Due.ToString("MMM d", CultureInfo.InvariantCulture).ToLowerInvariant());
        }
        else
        {
            parts.Add(item.Due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (item.Time is { } t)
        {
            parts.Add(t.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        switch (item.Recurrence)
        {
            case Recurrence.Daily: parts.Add("every day"); break;
            case Recurrence.Weekdays: parts.Add("every weekday"); break;
            case Recurrence.Weekly:
                // Naming the anchor weekday keeps it intact when Due was postponed off that day.
                parts.Add("every " + (item.RecurrenceDay ?? item.Due.DayOfWeek).ToString().ToLowerInvariant());
                break;
        }

        parts.Add(item.Title);
        return string.Join(' ', parts);
    }
}
