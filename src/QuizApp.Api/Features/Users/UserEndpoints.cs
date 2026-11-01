using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using QuizApp.Api.Data;
using QuizApp.Api.Domain;
using QuizApp.Api.Features.Common;

namespace QuizApp.Api.Features.Users;

public record CreateUserRequest(string? Name, string? Email);

public record UserDto(int Id, string Name, string Email, DateTime CreatedAt);

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");

        group.MapPost("/", Create);
        group.MapGet("/{id:int}", GetById).WithName("GetUserById");
        // History and stats are mapped in UserStatsEndpoints.

        return group;
    }

    private static async Task<IResult> Create(CreateUserRequest? request, QuizDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var email = NormalizeEmail(request!.Email!);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return ApiErrors.Conflict($"A user with email '{email}' already exists.");

        var user = new User { Name = request.Name!.Trim(), Email = email, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ApiErrors.IsUniqueViolation(ex))
        {
            // Lost a race with a concurrent create of the same email.
            return ApiErrors.Conflict($"A user with email '{email}' already exists.");
        }

        return Results.CreatedAtRoute("GetUserById", new { id = user.Id }, ToDto(user));
    }

    private static async Task<IResult> GetById(int id, QuizDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? ApiErrors.NotFound($"User {id} not found.") : Results.Ok(ToDto(user));
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    internal static Dictionary<string, string[]> Validate(CreateUserRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["Request body is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Name is required."];
        else if (request.Name.Trim().Length > 100) errors["name"] = ["Name must be at most 100 characters."];

        if (string.IsNullOrWhiteSpace(request.Email)) errors["email"] = ["Email is required."];
        else
        {
            var email = request.Email.Trim();
            if (email.Length > 254) errors["email"] = ["Email must be at most 254 characters."];
            else if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !parsed.Host.Contains('.'))
                errors["email"] = ["Email is not a valid address."];
        }

        return errors;
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Name, u.Email, u.CreatedAt);
}
