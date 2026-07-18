using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using study_buddy_quiz.Data;
using study_buddy_quiz.Dtos;
using study_buddy_quiz.Models;
using study_buddy_quiz.Services;

namespace study_buddy_quiz.Controllers;

[ApiController]
[Route("api/quizzes")]
public class QuizController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly QuizGenerationService _quizGenerationService;
    private readonly QuizContentExtractor _quizContentExtractor;

    public QuizController(
        ApplicationDbContext context,
        QuizGenerationService quizGenerationService,
        QuizContentExtractor quizContentExtractor)
    {
        _context = context;
        _quizGenerationService = quizGenerationService;
        _quizContentExtractor = quizContentExtractor;
    }

    [HttpPost("generate-text")]
    public ActionResult<QuizGenerationResponseDto> GenerateFromText([FromBody] QuizGenerationRequest request, [FromHeader(Name = "X-OpenAI-Api-Key")] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { message = "Podaj klucz OpenAI API." });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { message = "Treść wejściowa jest wymagana." });
        }

        var questionTypes = ParseQuestionTypes(request.QuestionTypes);

        var response = _quizGenerationService.GenerateFromText(
            request.Content,
            request.QuestionCount,
            questionTypes,
            apiKey);

        return Ok(response);
    }

    [HttpPost("generate-url")]
    public ActionResult<QuizGenerationResponseDto> GenerateFromUrl([FromBody] QuizGenerationRequest request, [FromHeader(Name = "X-OpenAI-Api-Key")] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { message = "Podaj klucz OpenAI API." });
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { message = "Adres URL jest wymagany." });
        }

        var questionTypes = ParseQuestionTypes(request.QuestionTypes);

        var response = _quizGenerationService.GenerateFromUrl(
            request.Url,
            request.QuestionCount,
            questionTypes,
            apiKey);

        return Ok(response);
    }

    [HttpPost("generate-file")]
    public ActionResult<QuizGenerationResponseDto> GenerateFromFile([FromForm] IFormFile file, [FromForm] int questionCount, [FromForm] string? questionTypes, [FromHeader(Name = "X-OpenAI-Api-Key")] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { message = "Podaj klucz OpenAI API." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Wgraj plik PDF, DOCX lub TXT." });
        }

        var content = _quizContentExtractor.ExtractText(file);
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { message = "Nie udało się odczytać treści z pliku." });
        }

        var parsedTypes = ParseQuestionTypes(questionTypes);
        var response = _quizGenerationService.GenerateFromFileContent(content, questionCount, parsedTypes, apiKey);

        return Ok(response);
    }

    [HttpPost("save")]
    public async Task<ActionResult<QuizGenerationResponseDto>> SaveQuiz([FromBody] SaveQuizRequest request)
    {
        if (request.Questions.Count == 0)
        {
            return BadRequest(new { message = "Quiz musi zawierać co najmniej jedno pytanie." });
        }

        var userId = GetUserId();

        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Nowy quiz" : request.Title,
            SourceType = request.SourceType,
            SourceText = request.SourceText,
            SourceUrl = request.SourceUrl,
            CreatedAt = DateTime.UtcNow,
            UserId = userId,
            Questions = request.Questions.Select(question => new Question
            {
                Id = Guid.NewGuid(),
                Text = question.Text,
                Type = question.Type,
                Options = question.Options,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation,
                IsApproved = question.IsApproved
            }).ToList()
        };

        foreach (var question in quiz.Questions)
        {
            question.QuizId = quiz.Id;
        }

        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        return Ok(new QuizGenerationResponseDto
        {
            QuizId = quiz.Id,
            Title = quiz.Title,
            SourceType = quiz.SourceType,
            Questions = request.Questions
        });
    }

    [HttpPatch("{quizId:guid}/questions/{questionId:guid}/approve")]
    public async Task<IActionResult> ApproveQuestion(Guid quizId, Guid questionId, [FromBody] UpdateQuestionDecisionRequest request)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(x => x.QuizId == quizId && x.Id == questionId);

        if (question is null)
        {
            return NotFound(new { message = "Pytanie nie zostało znalezione." });
        }

        question.IsApproved = request.Approved;
        await _context.SaveChangesAsync();

        return Ok(new { message = request.Approved ? "Pytanie zatwierdzone." : "Pytanie odrzucone." });
    }

    [HttpDelete("{quizId:guid}/questions/{questionId:guid}")]
    public async Task<IActionResult> DeleteQuestion(Guid quizId, Guid questionId)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(x => x.QuizId == quizId && x.Id == questionId);

        if (question is null)
        {
            return NotFound(new { message = "Pytanie nie zostało znalezione." });
        }

        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Pytanie usunięte." });
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<object>>> GetHistory()
    {
        var userId = GetUserId();

        var query = _context.Quizzes
            .Include(x => x.Questions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(x => x.UserId == userId);

        var quizzes = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.SourceType,
                x.CreatedAt,
                QuestionsCount = x.Questions.Count,
                ApprovedQuestionsCount = x.Questions.Count(q => q.IsApproved == true)
            })
            .ToListAsync();

        return Ok(quizzes);
    }

    private string? GetUserId()
    {
        return Request.Headers.Authorization.FirstOrDefault()
            is string authHeader && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? DecodeSubFromJwt(authHeader[7..])
            : null;
    }

    private static string? DecodeSubFromJwt(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<QuestionType> ParseQuestionTypes(string? questionTypes)
    {
        if (string.IsNullOrWhiteSpace(questionTypes))
        {
            return new List<QuestionType>();
        }

        return ParseQuestionTypes(questionTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static List<QuestionType> ParseQuestionTypes(IEnumerable<string>? questionTypes)
    {
        if (questionTypes is null)
        {
            return new List<QuestionType>();
        }

        var types = questionTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => Enum.TryParse<QuestionType>(type, ignoreCase: true, out var parsed) ? parsed : QuestionType.MultipleChoice)
            .Distinct()
            .ToList();

        return types;
    }
}
