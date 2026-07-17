using study_buddy_quiz.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources
    .OfType<Microsoft.Extensions.Configuration.FileConfigurationSource>()
    .ToList()
    .ForEach(source => source.ReloadOnChange = false);

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();
app.ConfigureApplication();

app.Run();
