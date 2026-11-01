using QuizApp.Api.Domain;

namespace QuizApp.Tests.Domain;

public class PerformanceMessagesTests
{
    [Theory]
    [InlineData(100, PerformanceMessages.Perfect)]
    [InlineData(99.99, PerformanceMessages.Excellent)]
    [InlineData(80.01, PerformanceMessages.Excellent)]
    [InlineData(80, PerformanceMessages.Excellent)]
    [InlineData(79.99, PerformanceMessages.Good)]
    [InlineData(60.01, PerformanceMessages.Good)]
    [InlineData(60, PerformanceMessages.Good)]
    [InlineData(59.99, PerformanceMessages.Fair)]
    [InlineData(40.01, PerformanceMessages.Fair)]
    [InlineData(40, PerformanceMessages.Fair)]
    [InlineData(39.99, PerformanceMessages.Low)]
    [InlineData(0.01, PerformanceMessages.Low)]
    [InlineData(0, PerformanceMessages.Low)]
    public void For_ScoreAtBoundary_ReturnsExpectedBand(double score, string expected)
    {
        Assert.Equal(expected, PerformanceMessages.For(score));
    }

    [Fact]
    public void For_NegativeZero_ReturnsLow()
    {
        Assert.Equal(PerformanceMessages.Low, PerformanceMessages.For(-0.0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void For_OutOfRange_Throws(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceMessages.For(score));
    }

    [Fact]
    public void For_AllBands_AreDistinctMessages()
    {
        string[] all = [PerformanceMessages.Perfect, PerformanceMessages.Excellent, PerformanceMessages.Good,
            PerformanceMessages.Fair, PerformanceMessages.Low];

        Assert.Equal(all.Length, all.Distinct().Count());
    }
}
