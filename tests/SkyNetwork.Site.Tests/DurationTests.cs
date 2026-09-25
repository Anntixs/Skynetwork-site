using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

public class DurationTests
{
    [Theory]
    [InlineData("02:20", 140)]
    [InlineData("2:20", 140)]
    [InlineData("0220", 140)]
    [InlineData("220", 140)]
    [InlineData("0357", 237)]
    [InlineData("90", 90)]
    [InlineData("5", 5)]
    [InlineData("10:05", 605)]
    [InlineData(" 1:00 ", 60)]
    [InlineData("2h20", 140)]
    public void ReadsHoursAndMinutes(string text, int minutes)
    {
        Assert.True(Duration.TryParseMinutes(text, out var m));
        Assert.Equal(minutes, m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("02:60")]
    [InlineData("0299")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void RejectsNonsense(string text) => Assert.False(Duration.TryParseMinutes(text, out _));

    [Fact]
    public void FormatsAsClock()
    {
        Assert.Equal("02:20", Duration.Format(140));
        Assert.Equal("00:05", Duration.Format(5));
        Assert.Equal("", Duration.Format(0));
    }
}
