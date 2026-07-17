using Microsoft.EntityFrameworkCore;
using study_buddy_quiz.Data;

namespace study_buddy_quiz.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication ConfigureApplication(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.EnsureCreated();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors("FrontendPolicy");
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
