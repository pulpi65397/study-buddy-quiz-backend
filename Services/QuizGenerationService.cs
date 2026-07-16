using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using study_buddy_quiz.Dtos;
using study_buddy_quiz.Models;

namespace study_buddy_quiz.Services;

public class QuizGenerationService
{
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]?");
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public QuizGenerationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public QuizGenerationResponseDto GenerateFromText(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        var cleanedText = NormalizeInput(text);
        var sentences = ExtractSentences(cleanedText);
        var facts = sentences.Count > 0 ? sentences : new List<string> { cleanedText };

        var generatedQuestions = TryGenerateWithOpenAi(cleanedText, questionCount, questionTypes);
        if (generatedQuestions.Count == 0)
        {
            generatedQuestions = BuildFallbackQuestions(facts, questionCount, questionTypes);
        }

        return new QuizGenerationResponseDto
        {
            QuizId = Guid.NewGuid(),
            SourceType = "text",
            Title = "Quiz wygenerowany z tekstu",
            Questions = generatedQuestions
        };
    }

    public QuizGenerationResponseDto GenerateFromUrl(string url, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        var sourceText = $"Artykuł: {url}. Treść z linku została odczytana jako punkt startowy do generowania quizu.";
        return GenerateFromText(sourceText, questionCount, questionTypes);
    }

    public QuizGenerationResponseDto GenerateFromFileContent(string content, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        return GenerateFromText(content, questionCount, questionTypes);
    }

    private List<QuizGenerationQuestionDto> TryGenerateWithOpenAi(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        var apiKey = _configuration["OpenAI:ApiKey"] ?? _configuration["OpenAI__ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new List<QuizGenerationQuestionDto>();
        }

        var types = questionTypes.Any() ? questionTypes : new[] { QuestionType.MultipleChoice, QuestionType.TrueFalse };

        var prompt = $"Wygeneruj quiz z poniższego materiału. Zwróć TYLKO poprawny JSON bez markdownu. " +
                      "Schemat: { \"questions\": [{ \"text\": string, \"type\": string, \"options\": string[], \"correctAnswer\": string, \"explanation\": string, \"isApproved\": false }] }. " +
                      $"Liczba pytań: {Math.Max(1, questionCount)}. Typy pytań: {string.Join(", ", types)}. " +
                      $"Materiał: {text}";

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.4,
            messages = new[]
            {
                new { role = "system", content = "Odpowiadaj wyłącznie w czystym JSON zgodnym z podanym schematem." },
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = _httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return new List<QuizGenerationQuestionDto>();
            }

            using var document = JsonDocument.Parse(responseContent);
            var message = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(message))
            {
                return new List<QuizGenerationQuestionDto>();
            }

            var normalized = message.Trim();
            if (normalized.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            }

            var openAiResponse = JsonSerializer.Deserialize<OpenAiQuizResponse>(normalized, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });

            if (openAiResponse?.Questions is null || openAiResponse.Questions.Count == 0)
            {
                return new List<QuizGenerationQuestionDto>();
            }

            return openAiResponse.Questions.Select(question => new QuizGenerationQuestionDto
            {
                Id = Guid.NewGuid(),
                Text = question.Text,
                Type = question.Type,
                Options = question.Options,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation,
                IsApproved = question.IsApproved
            }).ToList();
        }
        catch
        {
            return new List<QuizGenerationQuestionDto>();
        }
    }

    private static List<QuizGenerationQuestionDto> BuildFallbackQuestions(List<string> facts, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        var effectiveTypes = questionTypes.Any() ? questionTypes.ToArray() : new[] { QuestionType.MultipleChoice, QuestionType.TrueFalse };
        var generatedQuestions = new List<QuizGenerationQuestionDto>();

        foreach (var type in effectiveTypes)
        {
            if (generatedQuestions.Count >= Math.Max(1, questionCount))
            {
                break;
            }

            generatedQuestions.Add(CreateQuestion(type, facts));
        }

        return generatedQuestions;
    }

    private static QuizGenerationQuestionDto CreateQuestion(QuestionType type, List<string> facts)
    {
        return type switch
        {
            QuestionType.MultipleChoice => CreateMultipleChoiceQuestion(facts),
            QuestionType.TrueFalse => CreateTrueFalseQuestion(facts),
            QuestionType.Open => CreateOpenQuestion(facts),
            QuestionType.FillBlank => CreateFillBlankQuestion(facts),
            _ => CreateMultipleChoiceQuestion(facts)
        };
    }

    private static QuizGenerationQuestionDto CreateMultipleChoiceQuestion(List<string> facts)
    {
        var statement = facts.FirstOrDefault() ?? "Temat nie został rozpoznany.";
        var correctAnswer = ExtractKeyword(statement);
        var distractors = facts.Skip(1).Select(ExtractKeyword).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3).ToList();
        var options = new List<string> { correctAnswer };
        options.AddRange(distractors);

        if (options.Count < 4)
        {
            options.AddRange(new[] { "Opcja B", "Opcja C", "Opcja D" });
        }

        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Wybierz poprawną odpowiedź dotycząca: {statement}",
            Type = QuestionType.MultipleChoice,
            Options = options.Take(4).ToList(),
            CorrectAnswer = correctAnswer,
            Explanation = "To pytanie zostało wygenerowane w wersji MVP na podstawie dostarczonego źródła.",
            IsApproved = false
        };
    }

    private static QuizGenerationQuestionDto CreateTrueFalseQuestion(List<string> facts)
    {
        var statement = facts.FirstOrDefault() ?? "To stwierdzenie jest kluczowe dla omawianego materiału.";
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Czy poniższe zdanie jest prawdziwe? {statement}",
            Type = QuestionType.TrueFalse,
            Options = new List<string> { "Prawda", "Fałsz" },
            CorrectAnswer = "Prawda",
            Explanation = "Pytanie prawda/fałsz zostało wygenerowane automatycznie na podstawie treści wejściowej.",
            IsApproved = false
        };
    }

    private static QuizGenerationQuestionDto CreateOpenQuestion(List<string> facts)
    {
        var statement = facts.FirstOrDefault() ?? "Opisz główną ideę materiału.";
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Na podstawie tekstu wyjaśnij, o czym mowa w zdaniu: {statement}",
            Type = QuestionType.Open,
            CorrectAnswer = "Odpowiedź uczestnika zostanie oceniona ręcznie.",
            Explanation = "To pytanie otwarte wspiera własne wyjaśnienie przez użytkownika.",
            IsApproved = false
        };
    }

    private static QuizGenerationQuestionDto CreateFillBlankQuestion(List<string> facts)
    {
        var statement = facts.FirstOrDefault() ?? "Najważniejszy element opisywany w tekście to ___ .";
        var keyword = ExtractKeyword(statement);
        var blanked = statement.Replace(keyword, "___", StringComparison.OrdinalIgnoreCase);
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Uzupełnij lukę: {blanked}",
            Type = QuestionType.FillBlank,
            CorrectAnswer = keyword,
            Explanation = "To pytanie uzupełniania luk jest prostą wersją MVP.",
            IsApproved = false
        };
    }

    private static string ExtractKeyword(string sentence)
    {
        var cleaned = Regex.Replace(sentence, @"[^\p{L}\p{N}\s]", string.Empty);
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return words.FirstOrDefault() ?? "kluczowy element";
    }

    private static string NormalizeInput(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    private static List<string> ExtractSentences(string input)
    {
        var matches = SentenceRegex.Matches(input);
        return matches.Select(match => match.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private sealed class OpenAiQuizResponse
    {
        public List<QuizGenerationQuestionDto> Questions { get; set; } = new();
    }
}
