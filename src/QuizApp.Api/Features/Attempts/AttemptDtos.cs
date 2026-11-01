using QuizApp.Api.Domain;

namespace QuizApp.Api.Features.Attempts;

// Requests: nullable so missing fields become 400s instead of binding failures.
public record StartAttemptRequest(int? UserId, int? QuizId);

public record SubmitAttemptRequest(int? UserId, List<SubmittedAnswer?>? Answers);

public record SubmittedAnswer(int? QuestionId, int? OptionId);

/// <summary>Returned while an attempt is in progress: status only, no answer key.</summary>
public record AttemptStatusDto(
    int Id,
    int UserId,
    int QuizId,
    string QuizTitle,
    AttemptStatus Status,
    DateTime StartedAt,
    int TotalQuestions);

/// <summary>Returned once an attempt is completed: the score, a per-question breakdown with explanations, and the notification status.</summary>
public record AttemptResultDto(
    int Id,
    int UserId,
    int QuizId,
    string QuizTitle,
    AttemptStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int CorrectCount,
    int TotalQuestions,
    double Score,
    string Message,
    NotificationStatus? NotificationStatus,
    IReadOnlyList<QuestionResultDto> Questions);

public record QuestionResultDto(
    int QuestionId,
    int Order,
    string Text,
    int? SelectedOptionId,
    string? SelectedOptionText,
    int CorrectOptionId,
    string CorrectOptionText,
    bool IsCorrect,
    string Explanation);

/// <summary>One row in a user's history.</summary>
public record AttemptSummaryDto(
    int Id,
    int QuizId,
    string QuizTitle,
    AttemptStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? CorrectCount,
    int TotalQuestions,
    double? Score,
    NotificationStatus? NotificationStatus);

public record UserStatsDto(
    int UserId,
    int TotalAttempts,
    int CompletedAttempts,
    int InProgressAttempts,
    double? AverageScore,
    double? BestScore,
    DateTime? LastCompletedAt,
    IReadOnlyList<QuizStatsDto> Quizzes);

public record QuizStatsDto(
    int QuizId,
    string QuizTitle,
    int CompletedAttempts,
    double AverageScore,
    double BestScore,
    double LatestScore);
