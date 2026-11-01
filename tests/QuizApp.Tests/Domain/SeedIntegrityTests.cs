using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;

namespace QuizApp.Tests.Domain;

/// <summary>Guards against the answer key leaking through the seeded options themselves.</summary>
public sealed class SeedIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly List<Question> _questions;

    public SeedIntegrityTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<QuizDbContext>().UseSqlite(_connection).Options;
        using (var db = new QuizDbContext(options))
        {
            db.Database.EnsureCreated();
            DbSeeder.Seed(db);
        }

        using var read = new QuizDbContext(options);
        _questions = read.Questions.AsNoTracking().Include(q => q.Options).OrderBy(q => q.Id).ToList();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void EveryQuestion_HasFourOptions_ExactlyOneCorrect_AndAnExplanation()
    {
        Assert.Equal(12, _questions.Count);
        Assert.All(_questions, q =>
        {
            Assert.Equal(4, q.Options.Count);
            Assert.Single(q.Options, o => o.IsCorrect);
            Assert.False(string.IsNullOrWhiteSpace(q.Explanation));
        });
    }

    [Fact]
    public void CorrectOptionPositions_AreEvenlyDistributed()
    {
        var positions = _questions
            .Select(q => q.Options.OrderBy(o => o.Order).ToList().FindIndex(o => o.IsCorrect))
            .GroupBy(p => p)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(new Dictionary<int, int> { [0] = 3, [1] = 3, [2] = 3, [3] = 3 }, positions);
    }

    [Fact]
    public void PickingTheLongestOrShortestOption_DoesNotBeatChance()
    {
        // Chance is 12 / 4 = 3. A leak shows up as a strategy scoring far above that.
        int Score(Func<IEnumerable<Option>, Option> pick) => _questions.Count(q => pick(q.Options).IsCorrect);

        var longest = Score(os => os.MaxBy(o => o.Text.Length)!);
        var shortest = Score(os => os.MinBy(o => o.Text.Length)!);

        Assert.True(longest <= 4, $"'Pick the longest option' scores {longest}/12.");
        Assert.True(shortest <= 4, $"'Pick the shortest option' scores {shortest}/12.");
    }
}
