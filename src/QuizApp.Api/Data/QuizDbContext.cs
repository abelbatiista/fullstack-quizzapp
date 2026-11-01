using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QuizApp.Api.Domain;

namespace QuizApp.Api.Data;

public class QuizDbContext(DbContextOptions<QuizDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no DateTime kind: normalize to UTC on write, mark as UTC on read.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(254);
            e.HasIndex(u => u.Email).IsUnique();
        });

        b.Entity<Quiz>(e =>
        {
            e.Property(q => q.Title).HasMaxLength(200);
            e.Property(q => q.Description).HasMaxLength(1000);
            e.HasMany(q => q.Questions).WithOne(q => q.Quiz).HasForeignKey(q => q.QuizId);
        });

        b.Entity<Question>(e =>
        {
            e.Property(q => q.Text).HasMaxLength(500);
            e.Property(q => q.Explanation).HasMaxLength(2000);
            e.HasMany(q => q.Options).WithOne(o => o.Question).HasForeignKey(o => o.QuestionId);
        });

        b.Entity<Option>(e => e.Property(o => o.Text).HasMaxLength(300));

        b.Entity<Attempt>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20)
                // Concurrency token: two racing submits → one gets DbUpdateConcurrencyException → 409.
                .IsConcurrencyToken();
            e.HasOne(a => a.User).WithMany(u => u.Attempts).HasForeignKey(a => a.UserId);
            e.HasOne(a => a.Quiz).WithMany().HasForeignKey(a => a.QuizId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.UserId, a.StartedAt });
        });

        b.Entity<AttemptAnswer>(e =>
        {
            e.HasOne(a => a.Attempt).WithMany(a => a.Answers).HasForeignKey(a => a.AttemptId);
            e.HasOne(a => a.Question).WithMany().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Option).WithMany().HasForeignKey(a => a.OptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.AttemptId, a.QuestionId }).IsUnique();
        });

        b.Entity<Notification>(e =>
        {
            e.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.LastError).HasMaxLength(2000);
            e.HasOne(n => n.Attempt).WithOne(a => a.Notification)
                .HasForeignKey<Notification>(n => n.AttemptId);
            e.HasIndex(n => n.AttemptId).IsUnique();
            e.HasIndex(n => new { n.Status, n.NextAttemptAt });
        });
    }
}

public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue
        ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
        : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
