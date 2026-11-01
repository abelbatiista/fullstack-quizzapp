namespace QuizApp.Api.Domain;

/// <summary>The answer key for one question.</summary>
public record QuestionKey(int QuestionId, int CorrectOptionId);

/// <summary>The graded outcome for one question. SelectedOptionId is null when unanswered.</summary>
public record GradedAnswer(int QuestionId, int? SelectedOptionId, bool IsCorrect);

public record ScoreResult(int CorrectCount, int TotalQuestions, double Score);

/// <summary>Pure scoring functions. No EF, no I/O, no clock.</summary>
public static class Scoring
{
    /// <summary>
    /// Grades every question in the key. Questions missing from <paramref name="answers"/> are
    /// unanswered and count as incorrect. Answers for questions not in the key are ignored
    /// (callers validate them before grading).
    /// </summary>
    public static IReadOnlyList<GradedAnswer> Grade(
        IReadOnlyCollection<QuestionKey> key,
        IReadOnlyDictionary<int, int> answers)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(answers);

        return key
            .Select(k => answers.TryGetValue(k.QuestionId, out var selected)
                ? new GradedAnswer(k.QuestionId, selected, selected == k.CorrectOptionId)
                : new GradedAnswer(k.QuestionId, null, false))
            .ToList();
    }

    /// <summary>Percentage from 0 to 100, rounded to 2 decimals. Returns 0 when there are no questions.</summary>
    public static double CalculateScore(int correctCount, int totalQuestions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalQuestions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(correctCount, totalQuestions);

        if (totalQuestions == 0) return 0;
        return Math.Round(correctCount * 100.0 / totalQuestions, 2, MidpointRounding.AwayFromZero);
    }

    public static ScoreResult Summarize(IReadOnlyCollection<GradedAnswer> graded)
    {
        ArgumentNullException.ThrowIfNull(graded);
        var correct = graded.Count(g => g.IsCorrect);
        return new ScoreResult(correct, graded.Count, CalculateScore(correct, graded.Count));
    }
}

/// <summary>Pure mapping from score to an encouraging message.</summary>
public static class PerformanceMessages
{
    public const string Perfect = "Perfect score! You've mastered this topic.";
    public const string Excellent = "Excellent work! You have a strong grasp of this material.";
    public const string Good = "Good job! A quick review of the explanations will get you to the top.";
    public const string Fair = "Nice effort! Review the explanations and try again to level up.";
    public const string Low = "Every expert started somewhere. Read the explanations and give it another go!";

    /// <summary>Bands: 100, ≥80, ≥60, ≥40, below 40.</summary>
    public static string For(double score)
    {
        if (double.IsNaN(score) || score < 0 || score > 100)
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 0 and 100.");

        return score switch
        {
            100 => Perfect,
            >= 80 => Excellent,
            >= 60 => Good,
            >= 40 => Fair,
            _ => Low,
        };
    }
}
