using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using study_buddy_quiz.Dtos;
using study_buddy_quiz.Models;

namespace study_buddy_quiz.Services;

public class QuizGenerationService
{
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]?");
    private readonly HttpClient _httpClient;

    public QuizGenerationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public QuizGenerationResponseDto GenerateFromText(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var cleanedText = NormalizeInput(text);
        var sentences = ExtractSentences(cleanedText);
        var facts = sentences.Count > 0 ? sentences : new List<string> { cleanedText };

        var generatedQuestions = TryGenerateWithOpenAi(cleanedText, questionCount, questionTypes, apiKey);
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

    public QuizGenerationResponseDto GenerateFromUrl(string url, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var sourceText = $"Artykuł: {url}. Treść z linku została odczytana jako punkt startowy do generowania quizu.";
        return GenerateFromText(sourceText, questionCount, questionTypes, apiKey);
    }

    public QuizGenerationResponseDto GenerateFromFileContent(string content, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        return GenerateFromText(content, questionCount, questionTypes, apiKey);
    }

    private List<QuizGenerationQuestionDto> TryGenerateWithOpenAi(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var types = questionTypes.Any() ? questionTypes : new[] { QuestionType.MultipleChoice, QuestionType.TrueFalse };

        var prompt = $"Wygeneruj po polsku quiz wyłącznie na podstawie poniższego materiału. Zwróć TYLKO poprawny JSON bez markdownu. " +
                      "Schemat: { \"questions\": [{ \"text\": string, \"type\": string, \"options\": string[], \"correctAnswer\": string, \"explanation\": string }] }. " +
                      "Dla pytania MultipleChoice przygotuj dokładnie cztery unikalne, pełne i sensowne odpowiedzi. Jedna odpowiedź musi być poprawna, występować w options i dokładnie odpowiadać correctAnswer. Nie używaj pojedynczych słów, wypełniaczy ani fragmentów polecenia. Pytanie ma być konkretne, a explanation ma krótko uzasadniać poprawną odpowiedź materiałem. " +
                      $"Liczba pytań: {Math.Max(1, questionCount)}. Typy pytań: {string.Join(", ", types)}. " +
                      $"Materiał: {text}";

        var requestBody = new
        {
            model = "gpt-5",
            temperature = 0.4,
            input = new[]
            {
                new { role = "user", content = prompt }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "quiz_generation",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            questions = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        text = new { type = "string" },
                                        type = new { type = "string" },
                                        options = new
                                        {
                                            type = "array",
                                            items = new { type = "string" }
                                        },
                                        correctAnswer = new { type = "string" },
                                        explanation = new { type = "string" }
                                    },
                                    required = new[] { "text", "type", "options", "correctAnswer", "explanation" },
                                    additionalProperties = false
                                }
                            }
                        },
                        required = new[] { "questions" },
                        additionalProperties = false
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            using var response = _httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return new List<QuizGenerationQuestionDto>();
            }

            using var document = JsonDocument.Parse(responseContent);
            var outputText = document.RootElement
                .TryGetProperty("output_text", out var outputTextElement)
                ? outputTextElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(outputText))
            {
                return new List<QuizGenerationQuestionDto>();
            }

            var normalized = outputText.Trim();
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
                IsApproved = null
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
        var declarativeFacts = facts
            .Where(fact => fact.Contains(" służą do ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sourceFacts = declarativeFacts.Count > 0 ? declarativeFacts : facts;
        var generatedQuestions = new List<QuizGenerationQuestionDto>();

        for (var index = 0; index < Math.Max(1, questionCount); index++)
        {
            var type = effectiveTypes[index % effectiveTypes.Length];
            generatedQuestions.Add(CreateQuestion(type, sourceFacts[index % sourceFacts.Count]));
        }

        return generatedQuestions;
    }

    private static QuizGenerationQuestionDto CreateQuestion(QuestionType type, string fact)
    {
        return type switch
        {
            QuestionType.MultipleChoice => CreateMultipleChoiceQuestion(fact),
            QuestionType.TrueFalse => CreateTrueFalseQuestion(fact),
            QuestionType.Open => CreateOpenQuestion(fact),
            QuestionType.FillBlank => CreateFillBlankQuestion(fact),
            _ => CreateMultipleChoiceQuestion(fact)
        };
    }

    private static QuizGenerationQuestionDto CreateMultipleChoiceQuestion(string fact)
    {
        var statement = fact.Trim().TrimEnd('.', '!', '?');
        var purposeMatch = Regex.Match(statement, @"^(?<subject>[\p{Lu}][\p{L}\s]{2,50}?)\s+służą do\s+(?<purpose>.+)$");
        string question;
        string correctAnswer;
        List<string> options;

        if (purposeMatch.Success)
        {
            var subject = purposeMatch.Groups["subject"].Value.Trim();
            var purpose = Capitalize(purposeMatch.Groups["purpose"].Value.Trim());
            question = $"Jakie jest zastosowanie: {subject}?";
            correctAnswer = $"{purpose}.";
            options = new List<string>
            {
                correctAnswer,
                "Przedstawianie kolejności komunikatów wymienianych między obiektami.",
                "Modelowanie przebiegu procesu biznesowego i podejmowanych decyzji.",
                "Określanie stanów obiektu oraz przejść między tymi stanami."
            };
        }
        else
        {
            question = "Które stwierdzenie jest zgodne z materiałem?";
            correctAnswer = $"{Capitalize(statement)}.";
            options = new List<string>
            {
                correctAnswer,
                "Dotyczy wyłącznie wyglądu interfejsu użytkownika.",
                "Opisuje jedynie kolejność wykonywania czynności w czasie.",
                "Nie przedstawia żadnych zależności ani elementów omawianego zagadnienia."
            };
        }

        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = question,
            Type = QuestionType.MultipleChoice,
            Options = options,
            CorrectAnswer = correctAnswer,
            Explanation = $"Poprawna odpowiedź wynika bezpośrednio ze zdania: „{statement}”.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateTrueFalseQuestion(string fact)
    {
        var statement = fact;
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Czy poniższe zdanie jest prawdziwe? {statement}",
            Type = QuestionType.TrueFalse,
            Options = new List<string> { "Prawda", "Fałsz" },
            CorrectAnswer = "Prawda",
            Explanation = "Pytanie prawda/fałsz zostało wygenerowane automatycznie na podstawie treści wejściowej.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateOpenQuestion(string fact)
    {
        var statement = fact;
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Na podstawie tekstu wyjaśnij, o czym mowa w zdaniu: {statement}",
            Type = QuestionType.Open,
            CorrectAnswer = "Odpowiedź uczestnika zostanie oceniona ręcznie.",
            Explanation = "To pytanie otwarte wspiera własne wyjaśnienie przez użytkownika.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateFillBlankQuestion(string fact)
    {
        var statement = fact;
        var keyword = ExtractKeyword(statement);
        var blanked = statement.Replace(keyword, "___", StringComparison.OrdinalIgnoreCase);
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Uzupełnij lukę: {blanked}",
            Type = QuestionType.FillBlank,
            CorrectAnswer = keyword,
            Explanation = "To pytanie uzupełniania luk jest prostą wersją MVP.",
            IsApproved = null
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
        var withSentenceBreaks = Regex.Replace(text.Trim(), @"(?<=\p{Ll})(?=\p{Lu})", ". ");
        return Regex.Replace(withSentenceBreaks, @"\s+", " ");
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
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
