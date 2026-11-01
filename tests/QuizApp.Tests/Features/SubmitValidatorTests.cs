using QuizApp.Api.Features.Attempts;

namespace QuizApp.Tests.Features;

public class SubmitValidatorTests
{
    private const int Owner = 7;

    // Question 1 → options 11, 12; question 2 → options 21, 22; question 3 → options 31, 32.
    private static readonly IReadOnlyDictionary<int, IReadOnlySet<int>> Quiz = new Dictionary<int, IReadOnlySet<int>>
    {
        [1] = new HashSet<int> { 11, 12 },
        [2] = new HashSet<int> { 21, 22 },
        [3] = new HashSet<int> { 31, 32 },
    };

    private static Dictionary<string, string[]> Validate(SubmitAttemptRequest? request) =>
        SubmitValidator.Validate(request, Owner, Quiz);

    private static SubmitAttemptRequest Req(params SubmittedAnswer?[] answers) => new(Owner, answers.ToList());

    [Fact]
    public void Validate_NullBody_ReturnsBodyErrorOnly()
    {
        var errors = Validate(null);

        Assert.Equal(["body"], errors.Keys);
    }

    [Fact]
    public void Validate_NullUserId_ReturnsUserIdError()
    {
        var errors = Validate(new SubmitAttemptRequest(null, []));

        Assert.Equal(["userId"], errors.Keys);
    }

    [Fact]
    public void Validate_UserIdMismatch_ReturnsUserIdError()
    {
        var errors = Validate(new SubmitAttemptRequest(Owner + 1, []));

        Assert.Equal(["userId"], errors.Keys);
        Assert.Contains("owner", errors["userId"].Single());
    }

    [Fact]
    public void Validate_NullAnswers_ReturnsAnswersError()
    {
        var errors = Validate(new SubmitAttemptRequest(Owner, null));

        Assert.Equal(["answers"], errors.Keys);
    }

    [Fact]
    public void Validate_NullUserIdAndNullAnswers_ReturnsBothErrors()
    {
        var errors = Validate(new SubmitAttemptRequest(null, null));

        Assert.True(errors.ContainsKey("userId"));
        Assert.True(errors.ContainsKey("answers"));
    }

    [Fact]
    public void Validate_NullItem_ReturnsItemError()
    {
        var errors = Validate(Req(new SubmittedAnswer(1, 11), null));

        Assert.Equal(["answers[1]"], errors.Keys);
    }

    [Fact]
    public void Validate_NullQuestionId_ReturnsQuestionIdError()
    {
        var errors = Validate(Req(new SubmittedAnswer(null, 11)));

        Assert.Equal(["answers[0].questionId"], errors.Keys);
    }

    [Fact]
    public void Validate_NullOptionId_ReturnsOptionIdError()
    {
        var errors = Validate(Req(new SubmittedAnswer(1, null)));

        Assert.Equal(["answers[0].optionId"], errors.Keys);
    }

    [Fact]
    public void Validate_NullQuestionIdAndOptionId_ReturnsBothErrors()
    {
        var errors = Validate(Req(new SubmittedAnswer(null, null)));

        Assert.Equal(["answers[0].optionId", "answers[0].questionId"], errors.Keys.Order());
    }

    [Fact]
    public void Validate_ForeignQuestion_ReturnsQuestionIdError()
    {
        var errors = Validate(Req(new SubmittedAnswer(99, 11)));

        Assert.Equal(["answers[0].questionId"], errors.Keys);
        Assert.Contains("does not belong", errors["answers[0].questionId"].Single());
    }

    [Fact]
    public void Validate_OptionFromAnotherQuestion_ReturnsOptionIdError()
    {
        var errors = Validate(Req(new SubmittedAnswer(1, 21)));

        Assert.Equal(["answers[0].optionId"], errors.Keys);
    }

    [Fact]
    public void Validate_UnknownOption_ReturnsOptionIdError()
    {
        var errors = Validate(Req(new SubmittedAnswer(1, 999)));

        Assert.Equal(["answers[0].optionId"], errors.Keys);
    }

    [Fact]
    public void Validate_DuplicateQuestionId_ReturnsErrorOnSecondOccurrence()
    {
        var errors = Validate(Req(new SubmittedAnswer(1, 11), new SubmittedAnswer(1, 12)));

        Assert.Equal(["answers[1].questionId"], errors.Keys);
        Assert.Contains("more than once", errors["answers[1].questionId"].Single());
    }

    [Fact]
    public void Validate_EmptyAnswerList_IsValid()
    {
        Assert.Empty(Validate(Req()));
    }

    [Fact]
    public void Validate_PartialAnswers_IsValid()
    {
        Assert.Empty(Validate(Req(new SubmittedAnswer(1, 11), new SubmittedAnswer(3, 32))));
    }

    [Fact]
    public void Validate_AllQuestionsAnswered_IsValid()
    {
        Assert.Empty(Validate(Req(new SubmittedAnswer(1, 12), new SubmittedAnswer(2, 21), new SubmittedAnswer(3, 31))));
    }

    [Fact]
    public void Validate_MultipleBadItems_ReportsEachByIndex()
    {
        var errors = Validate(Req(
            new SubmittedAnswer(1, 11),   // ok
            new SubmittedAnswer(99, 1),   // foreign question
            new SubmittedAnswer(2, 31),   // foreign option
            null));                       // null item

        Assert.Equal(["answers[1].questionId", "answers[2].optionId", "answers[3]"], errors.Keys.Order());
    }
}
