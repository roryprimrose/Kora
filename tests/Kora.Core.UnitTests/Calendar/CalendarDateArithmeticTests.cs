using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Calendar;

namespace Kora.Core.UnitTests.Calendar;

public sealed class CalendarDateArithmeticTests
{
    [Theory]
    [InlineData("1900-02-28", "1900-03-01", 1)]
    [InlineData("2000-02-28", "2000-03-01", 2)]
    [InlineData("2024-02-29", "2024-03-01", 1)]
    [InlineData("2023-12-31", "2024-01-01", 1)]
    [InlineData("2024-04-30", "2024-05-01", 1)]
    [InlineData("0001-01-01", "9999-12-31", 3652058)]
    [InlineData("9999-12-31", "0001-01-01", -3652058)]
    [InlineData("2000-03-01", "2000-02-28", -2)]
    [InlineData("2000-02-29", "2000-02-29", 0)]
    public void Difference_and_add_are_signed_inverse_calendar_operations(string first, string last, int days)
    {
        var start = CalendarDateArithmetic.ParseDate(first);
        var end = CalendarDateArithmetic.ParseDate(last);
        CalendarDateArithmetic.Difference(start, end).Should().Be(days);
        CalendarDateArithmetic.Add(start, days).Should().Be(end);
        CalendarDateArithmetic.Add(end, -days).Should().Be(start);
        CalendarDateArithmetic.Add(start, 0).Should().Be(start);
        CalendarDateArithmetic.Format(end).Should().Be(last);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1900-02-29")]
    [InlineData("2001-02-29")]
    [InlineData("0000-01-01")]
    [InlineData("10000-01-01")]
    [InlineData("2024-13-01")]
    [InlineData("2024-04-31")]
    [InlineData("2024-2-29")]
    [InlineData("2024/02/29")]
    [InlineData(" 2024-02-29")]
    [InlineData("2024-02-29 ")]
    [InlineData("2024-02-29\n")]
    [InlineData("２０２４-０２-２９")]
    [InlineData("2024−02−29")]
    [InlineData("today")]
    [InlineData("2024-02-29T00:00:00Z")]
    public void Dates_require_canonical_full_ASCII_calendar_dates(string text)
    {
        var parse = () => CalendarDateArithmetic.ParseDate(text);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("-1", -1)]
    [InlineData("3652058", 3652058)]
    [InlineData("-3652058", -3652058)]
    public void Offsets_have_explicit_domain_derived_bound(string text, int expected)
    {
        CalendarDateArithmetic.MaximumDays.Should().Be(DateOnly.MaxValue.DayNumber - DateOnly.MinValue.DayNumber);
        CalendarDateArithmetic.ParseDays(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("1+1")]
    [InlineData("1,000")]
    [InlineData("١")]
    [InlineData("−1")]
    [InlineData("3652059")]
    [InlineData("-3652059")]
    [InlineData("2147483647")]
    [InlineData("99999999")]
    public void Noncanonical_and_unbounded_offsets_are_not_repaired(string text)
    {
        var parse = () => CalendarDateArithmetic.ParseDays(text);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("0001-01-01", -1)]
    [InlineData("9999-12-31", 1)]
    [InlineData("2000-02-29", int.MinValue)]
    [InlineData("2000-02-29", int.MaxValue)]
    [InlineData("2000-02-29", -3652058)]
    [InlineData("2000-02-29", 3652058)]
    public void Domain_overflow_is_explicit_without_clamping(string date, int days)
    {
        var add = () => CalendarDateArithmetic.Add(CalendarDateArithmetic.ParseDate(date), days);
        add.Should().Throw<InvalidDataException>().WithMessage("*outside*");
    }

    [Fact]
    public void Null_inputs_are_programming_errors()
    {
        var date = () => CalendarDateArithmetic.ParseDate(null!);
        var days = () => CalendarDateArithmetic.ParseDays(null!);
        date.Should().Throw<ArgumentNullException>();
        days.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("th-TH")]
    public void Arithmetic_and_format_are_invariant(string culture)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CalendarDateArithmetic.Format(CalendarDateArithmetic.Add(CalendarDateArithmetic.ParseDate("2000-02-28"),
                CalendarDateArithmetic.ParseDays("2"))).Should().Be("2000-03-01");
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}
