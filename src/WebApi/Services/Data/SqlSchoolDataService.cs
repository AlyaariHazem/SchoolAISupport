using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using WebApi.Configuration;
using WebApi.Infrastructure;

namespace WebApi.Services.Data;

/// <summary>
/// Queries MySchool tenant tables (<c>Students</c>, <c>Divisions</c>, <c>Classes</c>, <c>Attendances</c>) via SQL Server.
/// Column names follow EF Core owned-type pattern (<c>FullName_FirstName</c>, etc.).
/// Per-request connection: <see cref="SchoolDataConnection.HttpContextItemKey"/> (set when the client sends <c>tenantId</c>).
/// </summary>
public sealed class SqlSchoolDataService : ISchoolDataService
{
    private readonly string? _fallbackConnectionString;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SqlSchoolDataService(
        IOptions<SchoolDataOptions> options,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _fallbackConnectionString = options.Value.ConnectionString?.Trim();
        if (string.IsNullOrEmpty(_fallbackConnectionString))
            _fallbackConnectionString = configuration.GetConnectionString("TenantDesignTime")?.Trim();
        if (string.IsNullOrEmpty(_fallbackConnectionString))
            _fallbackConnectionString = configuration.GetConnectionString("TenantConnection")?.Trim();
    }

    /// <summary>True if a static fallback exists or the current request has a runtime tenant connection.</summary>
    public bool IsConfigured
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_fallbackConnectionString)) return true;
            var runtime = GetRuntimeConnectionString();
            return !string.IsNullOrWhiteSpace(runtime);
        }
    }

    private string? GetRuntimeConnectionString()
    {
        var item = _httpContextAccessor.HttpContext?.Items.TryGetValue(SchoolDataConnection.HttpContextItemKey, out var raw) == true
            ? raw
            : null;
        return item as string;
    }

    private string? ResolveConnectionString() =>
        GetRuntimeConnectionString() ?? _fallbackConnectionString;

    private SqlConnection CreateConnection()
    {
        var cs = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("School database is not configured.");
        return new SqlConnection(cs);
    }

    private static string NotConfiguredMessage() =>
        "The school database is not connected for this assistant. Send tenantId (MySchool tenant) on the chat request, " +
        "and configure the master database (SchoolData:MasterConnectionString or ConnectionStrings:SqlAdminConnection) so the tenant connection can be resolved. " +
        "Alternatively set SchoolData:ConnectionString or ConnectionStrings:TenantDesignTime for a fixed tenant (e.g. dev).";

    private static string DbErrorMessage(Exception ex) =>
        $"A database error occurred while querying school records: {ex.Message}. No figures should be assumed; try again or contact support.";

    public async Task<string> GetStudentsCountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM Students;", conn);
            var count = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
            return $"VERIFIED_FROM_DATABASE: Total student count = {count}.";
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    public async Task<string> GetClassesCountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();
        const string sql = """
            SELECT
                SUM(CASE WHEN [State] = 1 THEN 1 ELSE 0 END) AS ActiveCount,
                COUNT(*) AS TotalCount
            FROM Classes;
            """;
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return "No rows returned when counting classes.";

            var active = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
            var total = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            return $"VERIFIED_FROM_DATABASE: Class rows (Classes table): active (State=1) = {active}, total rows = {total}.";
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    public async Task<string> GetStudentByIdAsync(int studentId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();
        if (studentId <= 0)
            return "Invalid studentId. Provide a positive integer matching Students.StudentID.";

        const string sql = """
            SELECT s.StudentID,
                   s.FullName_FirstName,
                   s.FullName_MiddleName,
                   s.FullName_LastName,
                   s.DivisionID,
                   d.DivisionName,
                   c.ClassID,
                   c.ClassName
            FROM Students s
            INNER JOIN Divisions d ON s.DivisionID = d.DivisionID
            INNER JOIN Classes c ON d.ClassID = c.ClassID
            WHERE s.StudentID = @id;
            """;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = studentId;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return $"No student found with StudentID = {studentId}.";

            var first = reader.GetString(reader.GetOrdinal("FullName_FirstName"));
            var middle = reader.IsDBNull(reader.GetOrdinal("FullName_MiddleName"))
                ? ""
                : reader.GetString(reader.GetOrdinal("FullName_MiddleName"));
            var last = reader.GetString(reader.GetOrdinal("FullName_LastName"));
            var name = string.Join(" ", new[] { first, middle, last }.Where(static p => !string.IsNullOrWhiteSpace(p)));
            var division = reader.GetString(reader.GetOrdinal("DivisionName"));
            var className = reader.GetString(reader.GetOrdinal("ClassName"));
            var classId = reader.GetInt32(reader.GetOrdinal("ClassID"));
            var divisionId = reader.GetInt32(reader.GetOrdinal("DivisionID"));

            return $"""
                VERIFIED_FROM_DATABASE: StudentID={studentId}; Name={name}; DivisionID={divisionId} ({division}); ClassID={classId} ({className}).
                """;
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    public async Task<string> GetStudentsByClassAsync(int? classId, string? className, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();
        if ((classId == null || classId <= 0) && string.IsNullOrWhiteSpace(className))
            return "Provide either classId (Classes.ClassID) or className (exact match to Classes.ClassName in the database).";

        var sql = """
            SELECT s.StudentID,
                   s.FullName_FirstName,
                   s.FullName_MiddleName,
                   s.FullName_LastName,
                   d.DivisionName,
                   c.ClassID,
                   c.ClassName
            FROM Students s
            INNER JOIN Divisions d ON s.DivisionID = d.DivisionID
            INNER JOIN Classes c ON d.ClassID = c.ClassID
            WHERE 1 = 1
            """;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand { Connection = conn };

            if (classId is > 0)
            {
                sql += " AND c.ClassID = @classId";
                cmd.Parameters.Add("@classId", SqlDbType.Int).Value = classId.Value;
            }
            else
            {
                sql += " AND LTRIM(RTRIM(c.ClassName)) = LTRIM(RTRIM(@className))";
                cmd.Parameters.Add("@className", SqlDbType.NVarChar, 512).Value = className!.Trim();
            }

            sql += " ORDER BY s.StudentID;";
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var rows = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var sid = reader.GetInt32(reader.GetOrdinal("StudentID"));
                var first = reader.GetString(reader.GetOrdinal("FullName_FirstName"));
                var midOrd = reader.GetOrdinal("FullName_MiddleName");
                var middle = reader.IsDBNull(midOrd) ? "" : reader.GetString(midOrd);
                var last = reader.GetString(reader.GetOrdinal("FullName_LastName"));
                var nm = string.Join(" ", new[] { first, middle, last }.Where(static p => !string.IsNullOrWhiteSpace(p)));
                rows.Add($"ID {sid}: {nm}");
            }

            if (rows.Count == 0)
            {
                var filter = classId is > 0 ? $"ClassID={classId}" : $"ClassName='{className}'";
                return $"No students found for {filter}. The class may not exist or has no students.";
            }

            var header = classId is > 0 ? $"ClassID {classId}" : $"ClassName '{className}'";
            return $"VERIFIED_FROM_DATABASE: {rows.Count} student(s) in {header}:\n" + string.Join("\n", rows.Take(200))
                   + (rows.Count > 200 ? $"\n... and {rows.Count - 200} more (list truncated)." : "");
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    public async Task<string> GetAttendanceSummaryAsync(
        int? studentId,
        int? classId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();

        const string sql = """
            SELECT Status, COUNT(*) AS Cnt
            FROM Attendances
            WHERE (@studentId IS NULL OR StudentID = @studentId)
              AND (@classId IS NULL OR ClassID = @classId)
              AND (@from IS NULL OR AttendanceDate >= @from)
              AND (@to IS NULL OR AttendanceDate <= @to)
            GROUP BY Status
            ORDER BY Status;
            """;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@studentId", SqlDbType.Int).Value = studentId is > 0 ? studentId.Value : DBNull.Value;
            cmd.Parameters.Add("@classId", SqlDbType.Int).Value = classId is > 0 ? classId.Value : DBNull.Value;
            cmd.Parameters.Add("@from", SqlDbType.Date).Value = fromDate.HasValue ? fromDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            cmd.Parameters.Add("@to", SqlDbType.Date).Value = toDate.HasValue ? toDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var parts = new List<string>();
            var total = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var status = reader.GetInt32(reader.GetOrdinal("Status"));
                var cnt = reader.GetInt32(reader.GetOrdinal("Cnt"));
                total += cnt;
                var label = status switch
                {
                    0 => "Present",
                    1 => "Absent",
                    2 => "Late",
                    3 => "Excused",
                    _ => $"Status_{status}",
                };
                parts.Add($"{label}: {cnt}");
            }

            if (parts.Count == 0)
                return "No attendance rows matched the filters (or the Attendances table is empty for that range).";

            var filterDesc = BuildFilterDescription(studentId, classId, fromDate, toDate);
            return $"VERIFIED_FROM_DATABASE: Attendance summary ({filterDesc}). Total rows: {total}. Breakdown: " + string.Join(", ", parts) + ".";
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    public async Task<string> GetAbsenceCountAsync(
        int? studentId,
        int? classId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return NotConfiguredMessage();

        const string sql = """
            SELECT COUNT(*) FROM Attendances
            WHERE Status = 1
              AND (@studentId IS NULL OR StudentID = @studentId)
              AND (@classId IS NULL OR ClassID = @classId)
              AND (@from IS NULL OR AttendanceDate >= @from)
              AND (@to IS NULL OR AttendanceDate <= @to);
            """;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@studentId", SqlDbType.Int).Value = studentId is > 0 ? studentId.Value : DBNull.Value;
            cmd.Parameters.Add("@classId", SqlDbType.Int).Value = classId is > 0 ? classId.Value : DBNull.Value;
            cmd.Parameters.Add("@from", SqlDbType.Date).Value = fromDate.HasValue ? fromDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            cmd.Parameters.Add("@to", SqlDbType.Date).Value = toDate.HasValue ? toDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

            var count = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
            var filterDesc = BuildFilterDescription(studentId, classId, fromDate, toDate);
            return $"VERIFIED_FROM_DATABASE: Absence count (Status=Absent) for {filterDesc}: {count}.";
        }
        catch (Exception ex)
        {
            return DbErrorMessage(ex);
        }
    }

    private static string BuildFilterDescription(int? studentId, int? classId, DateOnly? fromDate, DateOnly? toDate)
    {
        var bits = new List<string>();
        if (studentId is > 0) bits.Add($"StudentID={studentId}");
        if (classId is > 0) bits.Add($"ClassID={classId}");
        if (fromDate.HasValue) bits.Add($"from={fromDate:yyyy-MM-dd}");
        if (toDate.HasValue) bits.Add($"to={toDate:yyyy-MM-dd}");
        return bits.Count == 0 ? "all records" : string.Join(", ", bits);
    }
}
