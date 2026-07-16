using System.ComponentModel.DataAnnotations;

namespace study_buddy_quiz.Models;

public class Quiz
{
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string SourceType { get; set; } = string.Empty;

    public string? SourceText { get; set; }

    public string? SourceUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    [Required]
    public string Text { get; set; } = string.Empty;

    [Required]
    public QuestionType Type { get; set; }

    public List<string> Options { get; set; } = new();

    public string CorrectAnswer { get; set; } = string.Empty;

    public string? Explanation { get; set; }

    public bool IsApproved { get; set; }

    public Quiz Quiz { get; set; } = null!;
}

public enum QuestionType
{
    MultipleChoice,
    TrueFalse,
    Open,
    FillBlank
}
