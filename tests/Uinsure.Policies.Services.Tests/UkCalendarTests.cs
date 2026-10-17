using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// "Today" is the Europe/London date of the UTC instant. In 2027, BST runs from 01:00 UTC on Sunday
// 28 March to 01:00 UTC on Sunday 31 October. Each side of every boundary is its own test.
public sealed class UkCalendarTests
{
    [Fact]
    public void UkCalendar_today_at_23_30_UTC_in_BST_is_the_next_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 7, 1), UkTodayAt(2027, 6, 30, 23, 30));
    }

    [Fact]
    public void UkCalendar_today_at_23_30_UTC_in_GMT_is_the_same_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 1, 15), UkTodayAt(2027, 1, 15, 23, 30));
    }

    [Fact]
    public void UkCalendar_today_at_22_59_59_UTC_in_BST_is_still_the_UTC_date()
    {
        Assert.Equal(new DateOnly(2027, 6, 30), UkTodayAt(2027, 6, 30, 22, 59, 59));
    }

    [Fact]
    public void UkCalendar_today_at_23_00_UTC_in_BST_is_the_next_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 7, 1), UkTodayAt(2027, 6, 30, 23, 0));
    }

    [Fact]
    public void UkCalendar_today_at_23_59_59_UTC_in_GMT_is_the_same_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 1, 15), UkTodayAt(2027, 1, 15, 23, 59, 59));
    }

    [Fact]
    public void UkCalendar_today_at_midnight_UTC_in_GMT_is_the_new_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 1, 16), UkTodayAt(2027, 1, 16, 0, 0));
    }

    [Fact]
    public void UkCalendar_today_at_23_00_UTC_the_day_before_BST_starts_is_the_same_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 3, 27), UkTodayAt(2027, 3, 27, 23, 0));
    }

    [Fact]
    public void UkCalendar_today_at_23_00_UTC_the_day_BST_starts_is_the_next_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 3, 29), UkTodayAt(2027, 3, 28, 23, 0));
    }

    [Fact]
    public void UkCalendar_today_at_23_00_UTC_the_day_before_GMT_returns_is_the_next_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 10, 31), UkTodayAt(2027, 10, 30, 23, 0));
    }

    [Fact]
    public void UkCalendar_today_at_23_00_UTC_the_day_GMT_returns_is_the_same_UK_date()
    {
        Assert.Equal(new DateOnly(2027, 10, 31), UkTodayAt(2027, 10, 31, 23, 0));
    }

    private static DateOnly UkTodayAt(int year, int month, int day, int hour, int minute, int second = 0) =>
        UkCalendar.Today(new FakeTimeProvider(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero)));
}
