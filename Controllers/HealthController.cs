using Microsoft.AspNetCore.Mvc;

namespace study_buddy_quiz.Controllers;

[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "ok",
            message = "Service is running"
        });
    }
}
