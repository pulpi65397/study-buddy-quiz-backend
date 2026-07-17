using Microsoft.AspNetCore.Mvc;

namespace study_buddy_quiz.Controllers;

[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    [HttpGet("")]
    public ContentResult GetRoot()
    {
        return Content("<html><body><h1>Study Buddy Quiz API</h1><p>Status: running</p><ul><li><a href='/health'>/health</a></li><li><a href='/api/quizzes/history'>/api/quizzes/history</a></li></ul></body></html>", "text/html");
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "ok", message = "Service is running" });
    }
}
