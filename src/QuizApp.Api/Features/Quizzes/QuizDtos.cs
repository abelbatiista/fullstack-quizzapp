namespace QuizApp.Api.Features.Quizzes;

// Public DTOs: never expose Option.IsCorrect or Question.Explanation.
public record QuizSummaryDto(int Id, string Title, string Description, int QuestionCount);

public record QuizDetailDto(int Id, string Title, string Description, IReadOnlyList<QuestionDto> Questions);

public record QuestionDto(int Id, int Order, string Text, IReadOnlyList<OptionDto> Options);

public record OptionDto(int Id, string Text);

// Requests: nullable so missing fields become 400s instead of binding failures.
public record CreateQuizRequest(string? Title, string? Description, List<CreateQuestionRequest>? Questions);

public record CreateQuestionRequest(string? Text, string? Explanation, List<CreateOptionRequest>? Options);

public record CreateOptionRequest(string? Text, bool IsCorrect);
