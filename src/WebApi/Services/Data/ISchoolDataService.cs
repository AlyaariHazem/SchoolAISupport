namespace WebApi.Services.Data;

/// <summary>
/// Read-only access to school records for AI tools. Implementations must query the database — never invent figures.
/// </summary>
public interface ISchoolDataService
{
    /// <summary>True when a connection string is configured (tools may still fail at runtime if DB is unreachable).</summary>
    bool IsConfigured { get; }

    Task<string> GetStudentsCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Rows in Classes table (school grade/class entities). Active = State = 1.</summary>
    Task<string> GetClassesCountAsync(CancellationToken cancellationToken = default);

    Task<string> GetStudentByIdAsync(int studentId, CancellationToken cancellationToken = default);

    Task<string> GetStudentsByClassAsync(int? classId, string? className, CancellationToken cancellationToken = default);

    Task<string> GetAttendanceSummaryAsync(
        int? studentId,
        int? classId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default);

    Task<string> GetAbsenceCountAsync(
        int? studentId,
        int? classId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default);
}
