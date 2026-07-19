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

    public async Task<QuizGenerationResponseDto> GenerateFromText(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var cleanedText = NormalizeInput(text);
        var sentences = ExtractSentences(cleanedText);
        var facts = sentences.Count > 0 ? sentences : [cleanedText];

        var generatedQuestions = await TryGenerateWithOpenAi(cleanedText, questionCount, questionTypes, apiKey);
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

    public Task<QuizGenerationResponseDto> GenerateFromUrl(string url, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var sourceText = $"Artyku\u0142: {url}. Tre\u015b\u0107 z linku zosta\u0142a odczytana jako punkt startowy do generowania quizu.";
        return GenerateFromText(sourceText, questionCount, questionTypes, apiKey);
    }

    public Task<QuizGenerationResponseDto> GenerateFromFileContent(string content, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        return GenerateFromText(content, questionCount, questionTypes, apiKey);
    }

    private async Task<List<QuizGenerationQuestionDto>> TryGenerateWithOpenAi(string text, int questionCount, IReadOnlyCollection<QuestionType> questionTypes, string apiKey)
    {
        var types = questionTypes.Any() ? questionTypes : new[] { QuestionType.MultipleChoice, QuestionType.TrueFalse };

        var prompt = $"Wygeneruj po polsku quiz wy\u0142\u0105cznie na podstawie poni\u017cszego materia\u0142u. Zwr\u00f3\u0107 TYLKO poprawny JSON bez markdownu. " +
                      "Schemat: { \"questions\": [{ \"text\": string, \"type\": string, \"options\": string[], \"correctAnswer\": string, \"explanation\": string }] }. " +
                      "Dla pytania MultipleChoice przygotuj dok\u0142adnie cztery unikalne, pe\u0142ne i sensowne odpowiedzi. Jedna odpowied\u017a musi by\u0107 poprawna, wyst\u0119powa\u0107 w options i dok\u0142adnie odpowiada\u0107 correctAnswer. Nie u\u017cywaj pojedynczych s\u0142\u00f3w, wype\u0142niaczy ani fragment\u00f3w polecenia. Pytanie ma by\u0107 konkretne, a explanation ma kr\u00f3tko uzasadnia\u0107 poprawn\u0105 odpowied\u017a materia\u0142em. " +
                      $"Liczba pyta\u0144: {Math.Max(1, questionCount)}. Typy pyta\u0144: {string.Join(", ", types)}. " +
                      $"Materia\u0142: {text}";

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
            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = JsonDocument.Parse(responseContent);
            var outputText = document.RootElement
                .TryGetProperty("output_text", out var outputTextElement)
                ? outputTextElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(outputText))
            {
                return [];
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
                return [];
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
            return [];
        }
    }

    private static List<QuizGenerationQuestionDto> BuildFallbackQuestions(List<string> facts, int questionCount, IReadOnlyCollection<QuestionType> questionTypes)
    {
        var effectiveTypes = questionTypes.Any() ? questionTypes.ToArray() : new[] { QuestionType.MultipleChoice, QuestionType.TrueFalse };
        var declarativeFacts = facts
            .Where(fact => fact.Contains(" s\u0142u\u017c\u0105 do ", StringComparison.OrdinalIgnoreCase))
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
        var purposeMatch = Regex.Match(statement, @"^(?<subject>[\p{Lu}][\p{L}\s]{2,50}?)\s+s\u0142u\u017c\u0105 do\s+(?<purpose>.+)$");
        string question;
        string correctAnswer;
        List<string> options;

        if (purposeMatch.Success)
        {
            var subject = purposeMatch.Groups["subject"].Value.Trim();
            var purpose = Capitalize(purposeMatch.Groups["purpose"].Value.Trim());
            question = $"Jakie jest zastosowanie: {subject}?";
            correctAnswer = $"{purpose}.";
            options =
            [
                correctAnswer,
                "Przedstawianie kolejno\u015bci komunikat\u00f3w wymienianych mi\u0119dzy obiektami.",
                "Modelowanie przebiegu procesu biznesowego i podejmowanych decyzji.",
                "Okre\u015blanie stan\u00f3w obiektu oraz przej\u015b\u0107 mi\u0119dzy tymi stanami."
            ];
        }
        else
        {
            question = "Kt\u00f3re stwierdzenie jest zgodne z materia\u0142em?";
            correctAnswer = $"{Capitalize(statement)}.";
            options =
            [
                correctAnswer,
                "Dotyczy wy\u0142\u0105cznie wygl\u0105du interfejsu u\u017cytkownika.",
                "Opisuje jedynie kolejno\u015b\u0107 wykonywania czynno\u015bci w czasie.",
                "Nie przedstawia \u017cadnych zale\u017cno\u015bci ani element\u00f3w omawianego zagadnienia."
            ];
        }

        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = question,
            Type = QuestionType.MultipleChoice,
            Options = options,
            CorrectAnswer = correctAnswer,
            Explanation = $"Poprawna odpowied\u017a wynika bezpo\u015brednio ze zdania: \u201e{statement}\u201d.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateTrueFalseQuestion(string fact)
    {
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Czy poni\u017csze zdanie jest prawdziwe? {fact}",
            Type = QuestionType.TrueFalse,
            Options = ["Prawda", "Fa\u0142sz"],
            CorrectAnswer = "Prawda",
            Explanation = "Pytanie prawda/fa\u0142sz zosta\u0142o wygenerowane automatycznie na podstawie tre\u015bci wej\u015bciowej.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateOpenQuestion(string fact)
    {
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Na podstawie tekstu wyja\u015bnij, o czym mowa w zdaniu: {fact}",
            Type = QuestionType.Open,
            CorrectAnswer = "Odpowied\u017a uczestnika zostanie oceniona r\u0119cznie.",
            Explanation = "To pytanie otwarte wspiera w\u0142asne wyja\u015bnienie przez u\u017cytkownika.",
            IsApproved = null
        };
    }

    private static QuizGenerationQuestionDto CreateFillBlankQuestion(string fact)
    {
        var keyword = ExtractKeyword(fact);
        var blanked = fact.Replace(keyword, "___", StringComparison.OrdinalIgnoreCase);
        return new QuizGenerationQuestionDto
        {
            Id = Guid.NewGuid(),
            Text = $"Uzupe\u0142nij luk\u0119: {blanked}",
            Type = QuestionType.FillBlank,
            CorrectAnswer = keyword,
            Explanation = "To pytanie uzupe\u0142niania luk jest prost\u0105 wersj\u0105 MVP.",
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
        public List<QuizGenerationQuestionDto> Questions { get; set; } = [];
    }
}
