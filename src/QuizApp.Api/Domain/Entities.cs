namespace QuizApp.Api.Domain;

public enum AttemptStatus { InProgress, Completed }

/// <summary>Pending → Sending (claimed, with a lease) → Sent | Pending (retry) | Failed.</summary>
public enum NotificationStatus { Pending, Sending, Sent, Failed }

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Attempt> Attempts { get; set; } = [];
}

public class Quiz
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public List<Question> Questions { get; set; } = [];
}

public class Question
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public required string Text { get; set; }
    public int Order { get; set; }
    public required string Explanation { get; set; }

    public Quiz Quiz { get; set; } = null!;
    public List<Option> Options { get; set; } = [];
}

public class Option
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public required string Text { get; set; }
    public int Order { get; set; }
    public bool IsCorrect { get; set; }

    public Question Question { get; set; } = null!;
}

public class Attempt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int QuizId { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CorrectCount { get; set; }
    public int TotalQuestions { get; set; }

    /// <summary>Percentage 0–100. Double because EF Core SQLite can't aggregate decimal.</summary>
    public double Score { get; set; }

    public User User { get; set; } = null!;
    public Quiz Quiz { get; set; } = null!;
    public List<AttemptAnswer> Answers { get; set; } = [];
    public Notification? Notification { get; set; }
}

public class AttemptAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public int QuestionId { get; set; }

    /// <summary>Null = unanswered (counts as incorrect).</summary>
    public int? OptionId { get; set; }
    public bool IsCorrect { get; set; }

    public Attempt Attempt { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public Option? Option { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public Attempt Attempt { get; set; } = null!;
}
