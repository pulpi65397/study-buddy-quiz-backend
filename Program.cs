using study_buddy_quiz.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();
app.ConfigureApplication();

app.Run();
