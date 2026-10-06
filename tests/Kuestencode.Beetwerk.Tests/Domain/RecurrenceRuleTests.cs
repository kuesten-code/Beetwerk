using Kuestencode.Beetwerk.Domain.Enums;
using Kuestencode.Beetwerk.Domain.Recurrence;

namespace Kuestencode.Beetwerk.Tests.Domain;

public class RecurrenceRuleTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static RecurrenceRule Rule(RecurrenceFrequency frequency, int interval, DateOnly start, int? from = null, int? to = null) =>
        new(frequency, interval, start, from, to);

    [Fact]
    public void Daily_returns_next_day_on_grid()
    {
        var rule = Rule(RecurrenceFrequency.Daily, 3, D(2026, 5, 1));
        Assert.Equal(D(2026, 5, 4), rule.NextAfter(D(2026, 5, 1)));
        Assert.Equal(D(2026, 5, 7), rule.NextAfter(D(2026, 5, 5)));
    }

    [Fact]
    public void Weekly_keeps_weekday()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, 2, D(2026, 10, 5));
        Assert.Equal(D(2026, 10, 19), rule.NextAfter(D(2026, 10, 5)));
        Assert.Equal(DayOfWeek.Monday, rule.NextAfter(D(2027, 3, 1))!.Value.DayOfWeek);
    }

    [Fact]
    public void Monthly_skips_months_without_that_day_like_rrule()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, 1, D(2026, 1, 31));
        Assert.Equal(D(2026, 3, 31), rule.NextAfter(D(2026, 1, 31)));
        Assert.Equal(D(2026, 5, 31), rule.NextAfter(D(2026, 3, 31)));
    }

    [Fact]
    public void Monthly_with_interval_stays_on_anchor_grid()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, 3, D(2026, 1, 15));
        Assert.Equal(D(2026, 4, 15), rule.NextAfter(D(2026, 1, 15)));
        Assert.Equal(D(2026, 10, 15), rule.NextAfter(D(2026, 8, 1)));
    }

    [Fact]
    public void Yearly_on_leap_day_only_occurs_in_leap_years()
    {
        var rule = Rule(RecurrenceFrequency.Yearly, 1, D(2028, 2, 29));
        Assert.Equal(D(2032, 2, 29), rule.NextAfter(D(2028, 2, 29)));
    }

    [Fact]
    public void Season_window_filters_occurrences()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, 1, D(2026, 5, 25), 3, 5);
        Assert.Equal(D(2027, 3, 1), rule.NextAfter(D(2026, 5, 25)));
    }

    [Fact]
    public void Season_window_can_wrap_around_new_year()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, 1, D(2026, 11, 1), 11, 2);
        Assert.True(rule.IsInSeason(D(2027, 1, 10)));
        Assert.False(rule.IsInSeason(D(2027, 6, 10)));
        Assert.Equal(D(2027, 11, 1), rule.NextAfter(D(2027, 2, 1)));
    }

    [Fact]
    public void NextInstance_skips_missed_occurrences_when_completed_late()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, 1, D(2026, 9, 7));
        Assert.Equal(D(2026, 10, 5), rule.NextInstance(D(2026, 9, 7), notBefore: D(2026, 10, 3)));
        Assert.Equal(D(2026, 10, 5), rule.NextInstance(D(2026, 9, 7), notBefore: D(2026, 10, 5)));
    }

    [Fact]
    public void NextInstance_follows_due_date_when_completed_early()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, 1, D(2026, 10, 10));
        Assert.Equal(D(2026, 11, 10), rule.NextInstance(D(2026, 10, 10), notBefore: D(2026, 10, 1)));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, null, null, "FREQ=DAILY;INTERVAL=1")]
    [InlineData(RecurrenceFrequency.Weekly, 2, 3, 5, "FREQ=WEEKLY;INTERVAL=2;BYMONTH=3,4,5")]
    [InlineData(RecurrenceFrequency.Monthly, 1, 11, 2, "FREQ=MONTHLY;INTERVAL=1;BYMONTH=1,2,11,12")]
    public void ToRRule_maps_to_icalendar(RecurrenceFrequency frequency, int interval, int? from, int? to, string expected)
    {
        var start = from is { } month ? D(2026, month, 1) : D(2026, 1, 1);
        Assert.Equal(expected, Rule(frequency, interval, start, from, to).ToRRule());
    }

    [Theory]
    [InlineData(RecurrenceFrequency.None, 1, null, null)]
    [InlineData(RecurrenceFrequency.Daily, 0, null, null)]
    [InlineData(RecurrenceFrequency.Daily, 1, 3, null)]
    [InlineData(RecurrenceFrequency.Daily, 1, 13, 5)]
    [InlineData(RecurrenceFrequency.Yearly, 1, 3, 5)]
    [InlineData(RecurrenceFrequency.Weekly, 1, 6, 8)]
    public void Validate_rejects_invalid_rules(RecurrenceFrequency frequency, int interval, int? from, int? to)
    {
        Assert.NotNull(Rule(frequency, interval, D(2026, 3, 2), from, to).Validate());
    }

    [Fact]
    public void Validate_accepts_valid_rule()
    {
        Assert.Null(Rule(RecurrenceFrequency.Weekly, 1, D(2026, 4, 6), 3, 5).Validate());
    }
}
