using QuizApp.Api.Domain;

namespace QuizApp.Tests.Domain;

public class ScoringTests
{
    // Six questions: question ids 1..6, correct option id = questionId * 10.
    private static readonly QuestionKey[] SixQuestionKey =
        Enumerable.Range(1, 6).Select(q => new QuestionKey(q, q * 10)).ToArray();

    private static Dictionary<int, int> CorrectAnswers(params int[] questionIds) =>
        questionIds.ToDictionary(q => q, q => q * 10);

    [Fact]
    public void Grade_AllCorrect_EveryAnswerIsCorrect()
    {
        var graded = Scoring.Grade(SixQuestionKey, CorrectAnswers(1, 2, 3, 4, 5, 6));
        var summary = Scoring.Summarize(graded);

        Assert.All(graded, g => Assert.True(g.IsCorrect));
        Assert.Equal(new ScoreResult(6, 6, 100), summary);
    }

    [Fact]
    public void Grade_NoneCorrect_ScoreIsZero()
    {
        var wrong = SixQuestionKey.ToDictionary(k => k.QuestionId, k => k.CorrectOptionId + 1);

        var summary = Scoring.Summarize(Scoring.Grade(SixQuestionKey, wrong));

        Assert.Equal(new ScoreResult(0, 6, 0), summary);
    }

    [Fact]
    public void Grade_FiveOfSixCorrect_ScoreIs83Point33()
    {
        var answers = CorrectAnswers(1, 2, 3, 4, 5);
        answers[6] = 999; // wrong

        var summary = Scoring.Summarize(Scoring.Grade(SixQuestionKey, answers));

        Assert.Equal(5, summary.CorrectCount);
        Assert.Equal(6, summary.TotalQuestions);
        Assert.Equal(83.33, summary.Score);
    }

    [Fact]
    public void Grade_UnansweredQuestions_CountAsIncorrectWithNullSelection()
    {
        var graded = Scoring.Grade(SixQuestionKey, CorrectAnswers(1, 2));
        var summary = Scoring.Summarize(graded);

        Assert.Equal(6, graded.Count);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(6, summary.TotalQuestions);
        Assert.Equal(33.33, summary.Score);
        Assert.All(graded.Where(g => g.QuestionId > 2), g =>
        {
            Assert.False(g.IsCorrect);
            Assert.Null(g.SelectedOptionId);
        });
    }

    [Fact]
    public void Grade_EmptyAnswers_AllUnansweredScoreZero()
    {
        var summary = Scoring.Summarize(Scoring.Grade(SixQuestionKey, new Dictionary<int, int>()));

        Assert.Equal(new ScoreResult(0, 6, 0), summary);
    }

    [Fact]
    public void Grade_WrongOptionId_IsIncorrectAndKeepsSelection()
    {
        var graded = Scoring.Grade([new QuestionKey(1, 10)], new Dictionary<int, int> { [1] = 11 });

        var g = Assert.Single(graded);
        Assert.False(g.IsCorrect);
        Assert.Equal(11, g.SelectedOptionId);
    }

    [Fact]
    public void Grade_AnswerForQuestionNotInKey_IsIgnored()
    {
        var graded = Scoring.Grade([new QuestionKey(1, 10)], new Dictionary<int, int> { [1] = 10, [99] = 990 });

        var g = Assert.Single(graded);
        Assert.Equal(1, g.QuestionId);
        Assert.True(g.IsCorrect);
    }

    [Fact]
    public void Grade_EmptyQuiz_SummaryIsZero()
    {
        var summary = Scoring.Summarize(Scoring.Grade([], new Dictionary<int, int>()));

        Assert.Equal(new ScoreResult(0, 0, 0), summary);
    }

    [Fact]
    public void Grade_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Scoring.Grade(null!, new Dictionary<int, int>()));
        Assert.Throws<ArgumentNullException>(() => Scoring.Grade(SixQuestionKey, null!));
        Assert.Throws<ArgumentNullException>(() => Scoring.Summarize(null!));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 6, 0)]
    [InlineData(6, 6, 100)]
    [InlineData(1, 3, 33.33)]
    [InlineData(2, 3, 66.67)]
    [InlineData(4, 5, 80)]
    [InlineData(5, 6, 83.33)]
    [InlineData(1, 6, 16.67)]
    [InlineData(1, 8, 12.5)]   // 12.5 exactly, no truncation by integer division
    [InlineData(1, 2, 50)]
    [InlineData(1, 7, 14.29)]
    [InlineData(1, 200, 0.5)]
    [InlineData(1, 1000, 0.1)]
    public void CalculateScore_ValidInputs_ReturnsRoundedPercentage(int correct, int total, double expected)
    {
        Assert.Equal(expected, Scoring.CalculateScore(correct, total));
    }

    [Fact]
    public void CalculateScore_MidpointValue_RoundsAwayFromZero()
    {
        // 1/800 = 0.125% exactly (representable in binary): AwayFromZero gives 0.13, banker's would give 0.12.
        Assert.Equal(0.13, Scoring.CalculateScore(1, 800));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, -1)]
    [InlineData(6, 5)]
    [InlineData(1, 0)]
    public void CalculateScore_InvalidArgs_Throws(int correct, int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.CalculateScore(correct, total));
    }
}
