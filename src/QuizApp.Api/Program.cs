using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Features.Attempts;
using QuizApp.Api.Features.Quizzes;
using QuizApp.Api.Features.Users;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuizDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=quizapp.db"));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Time-boxed MVP: no migrations. Schema changes require deleting the .db file.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
    db.Database.EnsureCreated();
    DbSeeder.Seed(db);
}

app.MapGet("/", () => Results.Redirect("/quizzes")).ExcludeFromDescription();
app.MapQuizEndpoints();
app.MapUserEndpoints().MapUserStatsEndpoints();
app.MapAttemptEndpoints();

app.Run();

public partial class Program;
