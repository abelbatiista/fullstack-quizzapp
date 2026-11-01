using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuizApp.Api.Features.Common;

public static class ApiErrors
{
    public static IResult NotFound(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: detail);

    public static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);

    public static IResult BadRequest(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private const int SqliteConstraintUnique = 2067; // SQLITE_CONSTRAINT_UNIQUE

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique };
}
