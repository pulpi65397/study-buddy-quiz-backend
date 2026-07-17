using Microsoft.EntityFrameworkCore;
using study_buddy_quiz.Data;
using study_buddy_quiz.Services;

namespace study_buddy_quiz.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddHttpClient<QuizGenerationService>();
        services.AddScoped<QuizContentExtractor>();

        services.AddCors(options =>
        {
            options.AddPolicy("FrontendPolicy", policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                    {
                        if (string.IsNullOrWhiteSpace(origin))
                        {
                            return false;
                        }

                        return origin.StartsWith("http://localhost:517", StringComparison.OrdinalIgnoreCase)
                            || origin.StartsWith("http://127.0.0.1:517", StringComparison.OrdinalIgnoreCase);
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        return services;
    }
}
