namespace QuizApp.Api.Features.Attempts;

/// <summary>Pure validation of a submit request against the quiz structure. No EF.</summary>
public static class SubmitValidator
{
    /// <param name="request">The incoming request (may be null).</param>
    /// <param name="attemptUserId">The owner of the attempt.</param>
    /// <param name="optionsByQuestion">For every question in the quiz, the ids of its options.</param>
    /// <returns>Validation errors keyed by field; empty when valid.</returns>
    public static Dictionary<string, string[]> Validate(
        SubmitAttemptRequest? request,
        int attemptUserId,
        IReadOnlyDictionary<int, IReadOnlySet<int>> optionsByQuestion)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string key, string message) => errors[key] = [.. errors.GetValueOrDefault(key, []), message];

        if (request is null)
        {
            Add("body", "Request body is required.");
            return errors;
        }

        if (request.UserId is null) Add("userId", "userId is required.");
        else if (request.UserId != attemptUserId) Add("userId", "userId does not match the attempt's owner.");

        if (request.Answers is null)
        {
            Add("answers", "answers is required (send an empty list to submit with no answers).");
            return errors;
        }

        var seen = new HashSet<int>();
        for (var i = 0; i < request.Answers.Count; i++)
        {
            var a = request.Answers[i];
            var key = $"answers[{i}]";

            if (a is null) { Add(key, "Answer is required."); continue; }
            if (a.QuestionId is null) Add($"{key}.questionId", "questionId is required.");
            if (a.OptionId is null) Add($"{key}.optionId", "optionId is required (omit the question to leave it unanswered).");
            if (a.QuestionId is not { } questionId) continue;

            if (!seen.Add(questionId))
            {
                Add($"{key}.questionId", $"Question {questionId} is answered more than once.");
                continue;
            }

            if (!optionsByQuestion.TryGetValue(questionId, out var options))
            {
                Add($"{key}.questionId", $"Question {questionId} does not belong to this quiz.");
                continue;
            }

            if (a.OptionId is { } optionId && !options.Contains(optionId))
                Add($"{key}.optionId", $"Option {optionId} does not belong to question {questionId}.");
        }

        return errors;
    }
}
