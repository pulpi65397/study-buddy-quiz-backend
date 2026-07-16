using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace study_buddy_quiz.Services;

public class QuizContentExtractor
{
    public string ExtractText(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        return extension switch
        {
            ".txt" => ReadTextFile(file),
            ".md" => ReadTextFile(file),
            ".docx" => ReadDocxFile(file),
            ".pdf" => ReadPdfFile(file),
            _ => ReadTextFile(file)
        };
    }

    private static string ReadTextFile(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReadDocxFile(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body;
        return body?.InnerText ?? string.Empty;
    }

    private static string ReadPdfFile(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var document = PdfDocument.Open(stream);

        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            builder.Append(page.Text);
        }

        return builder.ToString();
    }
}
