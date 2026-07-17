# study-buddy-quiz-backend

REST API dla aplikacji Study Buddy Quiz. Zbudowane w ASP.NET Core 9, baza danych SQLite.

## Wymagania

- .NET 9 SDK
- Własny klucz OpenAI API (dostarczany przez użytkownika przy każdym żądaniu)

## Uruchomienie lokalne

```bash
dotnet run
```

API dostępne pod `http://localhost:5122`.

## Klucz OpenAI API

Aplikacja **nie przechowuje** klucza OpenAI API. Klucz jest przekazywany przez użytkownika w nagłówku każdego żądania generowania quizu:

```
X-OpenAI-Api-Key: sk-...
```

Klucz jest używany wyłącznie do wywołania OpenAI i nie jest nigdzie zapisywany.

## Endpointy

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| `POST` | `/api/quizzes/generate-text` | Generuj quiz z tekstu |
| `POST` | `/api/quizzes/generate-url` | Generuj quiz z adresu URL |
| `POST` | `/api/quizzes/generate-file` | Generuj quiz z pliku PDF/DOCX/TXT |
| `POST` | `/api/quizzes/save` | Zapisz quiz do bazy danych |
| `GET`  | `/api/quizzes/history` | Pobierz historię zapisanych quizów |
| `PATCH`| `/api/quizzes/{quizId}/questions/{questionId}/approve` | Zatwierdź lub odrzuć pytanie |
| `GET`  | `/health` | Health check |

## Zmienne środowiskowe

| Zmienna | Opis | Przykład |
|---------|------|---------|
| `ALLOWED_ORIGINS` | Dozwolone originy CORS (przecinek jako separator) | `https://twoja-aplikacja.vercel.app` |
| `ConnectionStrings__DefaultConnection` | Connection string do SQLite | `Data Source=study-buddy-quiz.db` |

## Wdrożenie (Render.com)

Projekt zawiera `Dockerfile` i `render.yaml` gotowe do wdrożenia. Ustaw zmienną `ALLOWED_ORIGINS` na adres URL frontendu z Vercel.
