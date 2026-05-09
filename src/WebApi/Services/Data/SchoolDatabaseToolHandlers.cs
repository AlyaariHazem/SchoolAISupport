using System.ComponentModel;

namespace WebApi.Services.Data;

/// <summary>
/// AI-callable wrappers around <see cref="ISchoolDataService"/>. Return strings only from database queries — never invent counts.
/// </summary>
public sealed class SchoolDatabaseToolHandlers(ISchoolDataService data)
{
    [Description(
        "Get the total number of students in the school database. Use for questions like 'how many students' / 'كم عدد الطلاب'. Returns an exact count from SQL.")]
    public Task<string> GetStudentsCountAsync(CancellationToken cancellationToken = default) =>
        data.GetStudentsCountAsync(cancellationToken);

    [Description(
        "Get counts of school classes (Classes table): active (State=1) and total. Use for 'how many classes' / 'كم عدد الصفوف' / grade-level rows.")]
    public Task<string> GetClassesCountAsync(CancellationToken cancellationToken = default) =>
        data.GetClassesCountAsync(cancellationToken);

    [Description(
        "Look up one student by primary key Students.StudentID. Returns name, division, and class from the database, or a clear not-found message.")]
    public Task<string> GetStudentByIdAsync(
        [Description("Students.StudentID (integer).")] int studentId,
        CancellationToken cancellationToken = default) =>
        data.GetStudentByIdAsync(studentId, cancellationToken);

    [Description(
        "List students in a class. Provide either classId (Classes.ClassID) or className (must match Classes.ClassName exactly as stored).")]
    public Task<string> GetStudentsByClassAsync(
        [Description("Optional Classes.ClassID. Omit if using className.")] int? classId,
        [Description("Optional exact Classes.ClassName. Omit if using classId.")] string? className,
        CancellationToken cancellationToken = default) =>
        data.GetStudentsByClassAsync(classId, className, cancellationToken);

    [Description(
        "Attendance breakdown by status (Present/Absent/Late/Excused) for optional filters: studentId, classId, date range (ISO yyyy-MM-dd). Omit filters to summarize all rows (may be large).")]
    public Task<string> GetAttendanceSummaryAsync(
        [Description("Optional filter by Students.StudentID.")] int? studentId,
        [Description("Optional filter by Classes.ClassID on attendance rows.")] int? classId,
        [Description("Optional start date inclusive (yyyy-MM-dd).")] string? fromDate,
        [Description("Optional end date inclusive (yyyy-MM-dd).")] string? toDate,
        CancellationToken cancellationToken = default)
    {
        var from = ParseDateOrNull(fromDate);
        var to = ParseDateOrNull(toDate);
        return data.GetAttendanceSummaryAsync(studentId, classId, from, to, cancellationToken);
    }

    [Description(
        "Count absence (Absent status only) rows in Attendances with optional studentId, classId, and date range (yyyy-MM-dd).")]
    public Task<string> GetAbsenceCountAsync(
        [Description("Optional filter by Students.StudentID.")] int? studentId,
        [Description("Optional filter by Classes.ClassID.")] int? classId,
        [Description("Optional start date inclusive (yyyy-MM-dd).")] string? fromDate,
        [Description("Optional end date inclusive (yyyy-MM-dd).")] string? toDate,
        CancellationToken cancellationToken = default)
    {
        var from = ParseDateOrNull(fromDate);
        var to = ParseDateOrNull(toDate);
        return data.GetAbsenceCountAsync(studentId, classId, from, to, cancellationToken);
    }

    private static DateOnly? ParseDateOrNull(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        return DateOnly.TryParse(iso.Trim(), out var d) ? d : null;
    }
}
