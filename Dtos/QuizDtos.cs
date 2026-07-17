using study_buddy_quiz.Models;

namespace study_buddy_quiz.Dtos;

public class QuizGenerationRequest
{
    public string? Content { get; set; }

    public string? Url { get; set; }

    public int QuestionCount { get; set; } = 5;

    public List<string> QuestionTypes { get; set; } = new();
}

public class QuizGenerationQuestionDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public List<string> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public bool? IsApproved { get; set; }
}

public class QuizGenerationResponseDto
{
    public Guid QuizId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<QuizGenerationQuestionDto> Questions { get; set; } = new();
}

public class UpdateQuestionDecisionRequest
{
    public bool Approved { get; set; }
}

public class SaveQuizRequest
{
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? SourceText { get; set; }
    public string? SourceUrl { get; set; }
    public List<QuizGenerationQuestionDto> Questions { get; set; } = new();
}
