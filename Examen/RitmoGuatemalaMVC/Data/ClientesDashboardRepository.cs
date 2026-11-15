using System.Data;
using Microsoft.Data.SqlClient;
using RitmoGuatemalaMVC.Models;

namespace RitmoGuatemalaMVC.Data;

public sealed class ClientesDashboardRepository(ReadOnlySqlConnectionFactory connectionFactory)
{
    private static readonly string[] MonthNames = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
    private static readonly string[] GenreColors = ["#FFC93C", "#EAF0FF", "#1F9D55", "#88D8A6", "#B58B22"];
    private static readonly string[] PlanColors = ["#FFC93C", "#EAF0FF", "#1F9D55"];

    public async Task<ClientesDashboardViewModel> GetDashboardAsync(
        int? requestedYear,
        int? requestedDepartmentId,
        int? requestedGenreId,
        DateOnly? requestedReleaseStart,
        DateOnly? requestedReleaseEnd,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var years = await ReadYearsAsync(connection, referenceDate, cancellationToken);
        var departments = await ReadOptionsAsync(connection, "SELECT DepartamentoID, Nombre FROM dbo.Departamentos ORDER BY Nombre;", cancellationToken);
        var genres = await ReadOptionsAsync(connection, "SELECT GeneroID, Nombre FROM dbo.Generos ORDER BY Nombre;", cancellationToken);

        var selectedYear = years.Contains(requestedYear ?? 0) ? requestedYear!.Value : years.DefaultIfEmpty(referenceDate.Year).Max();
        int? selectedDepartmentId = departments.Any(option => option.Id == requestedDepartmentId) ? requestedDepartmentId : null;
        int? selectedGenreId = genres.Any(option => option.Id == requestedGenreId) ? requestedGenreId : null;
        var releaseStart = requestedReleaseStart ?? new DateOnly(selectedYear, 11, 16);
        var releaseEnd = requestedReleaseEnd ?? referenceDate;
        if (releaseEnd > referenceDate)
        {
            releaseEnd = referenceDate;
        }

        var (monthlyRevenue, revenue, newSubscriptions) = await ReadMonthlyRevenueAsync(
            connection, selectedYear, selectedDepartmentId, selectedGenreId, referenceDate, cancellationToken);
        var revenueByGenre = await ReadRevenueByGenreAsync(
            connection, selectedYear, selectedDepartmentId, selectedGenreId, referenceDate, cancellationToken);
        var revenueByDepartment = await ReadRevenueByDepartmentAsync(
            connection, selectedYear, selectedDepartmentId, selectedGenreId, referenceDate, cancellationToken);
        var (activeUsers, registeredUsers, usersByPlan) = await ReadUsersAsync(
            connection, selectedDepartmentId, referenceDate, cancellationToken);
        var (plays, minutes, topSongs) = await ReadListeningAsync(
            connection, selectedYear, selectedDepartmentId, selectedGenreId, referenceDate, cancellationToken);
        var newReleases = releaseStart <= releaseEnd
            ? await ReadNewReleasesAsync(
                connection, releaseStart, releaseEnd, selectedDepartmentId, selectedGenreId, referenceDate, cancellationToken)
            : [];

        return new ClientesDashboardViewModel
        {
            ReferenceDate = referenceDate,
            SelectedYear = selectedYear,
            SelectedDepartmentId = selectedDepartmentId,
            SelectedGenreId = selectedGenreId,
            ReleaseStart = releaseStart,
            ReleaseEnd = releaseEnd,
            Years = years,
            Departments = departments,
            Genres = genres,
            Kpis = new DashboardKpis(revenue, newSubscriptions, activeUsers, registeredUsers, minutes, plays),
            MonthlyRevenue = monthlyRevenue,
            RevenueByGenre = revenueByGenre,
            RevenueByDepartment = revenueByDepartment,
            UsersByPlan = usersByPlan,
            TopSongs = topSongs,
            NewReleases = newReleases
        };
    }

    private static async Task<IReadOnlyList<int>> ReadYearsAsync(
        SqlConnection connection, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT DISTINCT Anio
            FROM dbo.FinanzasMensuales
            WHERE EOMONTH(DATEFROMPARTS(Anio, Mes, 1)) <= @ReferenceDate
            ORDER BY Anio;
            """, connection);
        AddDate(command, "@ReferenceDate", referenceDate);
        var years = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            years.Add(reader.GetInt32(0));
        }

        return years;
    }

    private static async Task<IReadOnlyList<DashboardFilterOption>> ReadOptionsAsync(
        SqlConnection connection, string query, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(query, connection);
        var options = new List<DashboardFilterOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new DashboardFilterOption(reader.GetInt32(0), reader.GetString(1)));
        }

        return options;
    }

    private static async Task<(IReadOnlyList<MonthlyRevenuePoint> Points, decimal Revenue, int Subscriptions)> ReadMonthlyRevenueAsync(
        SqlConnection connection, int year, int? departmentId, int? genreId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT Mes,
                   SUM(IngresosSuscripcionesGTQ),
                   SUM(IngresosPublicidadGTQ),
                   SUM(NuevasSuscripciones)
            FROM dbo.FinanzasMensuales
            WHERE Anio = @Year
              AND EOMONTH(DATEFROMPARTS(Anio, Mes, 1)) <= @ReferenceDate
              AND (@DepartmentId IS NULL OR DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR GeneroID = @GenreId)
            GROUP BY Mes
            ORDER BY Mes;
            """, connection);
        AddFinanceFilters(command, year, departmentId, genreId, referenceDate);

        var values = new Dictionary<int, (decimal Subscription, decimal Advertising, int Subscriptions)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                values[reader.GetInt32(0)] = (reader.GetDecimal(1), reader.GetDecimal(2), reader.GetInt32(3));
            }
        }

        var points = Enumerable.Range(1, 12)
            .Select(month => values.TryGetValue(month, out var value)
                ? new MonthlyRevenuePoint(MonthNames[month - 1], value.Subscription, value.Advertising)
                : new MonthlyRevenuePoint(MonthNames[month - 1], null, null))
            .ToArray();
        return (points, values.Values.Sum(value => value.Subscription + value.Advertising), values.Values.Sum(value => value.Subscriptions));
    }

    private static async Task<IReadOnlyList<RevenuePoint>> ReadRevenueByGenreAsync(
        SqlConnection connection, int year, int? departmentId, int? genreId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT g.GeneroID, g.Nombre,
                   SUM(f.IngresosSuscripcionesGTQ + f.IngresosPublicidadGTQ)
            FROM dbo.FinanzasMensuales AS f
            INNER JOIN dbo.Generos AS g ON g.GeneroID = f.GeneroID
            WHERE f.Anio = @Year
              AND EOMONTH(DATEFROMPARTS(f.Anio, f.Mes, 1)) <= @ReferenceDate
              AND (@DepartmentId IS NULL OR f.DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR f.GeneroID = @GenreId)
            GROUP BY g.GeneroID, g.Nombre
            ORDER BY g.GeneroID;
            """, connection);
        AddFinanceFilters(command, year, departmentId, genreId, referenceDate);

        var points = new List<RevenuePoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            points.Add(new RevenuePoint(reader.GetString(1), reader.GetDecimal(2), GenreColors[(id - 1) % GenreColors.Length]));
        }

        return points;
    }

    private static async Task<IReadOnlyList<RevenuePoint>> ReadRevenueByDepartmentAsync(
        SqlConnection connection, int year, int? departmentId, int? genreId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT d.DepartamentoID, d.Nombre,
                   SUM(f.IngresosSuscripcionesGTQ + f.IngresosPublicidadGTQ)
            FROM dbo.FinanzasMensuales AS f
            INNER JOIN dbo.Departamentos AS d ON d.DepartamentoID = f.DepartamentoID
            WHERE f.Anio = @Year
              AND EOMONTH(DATEFROMPARTS(f.Anio, f.Mes, 1)) <= @ReferenceDate
              AND (@DepartmentId IS NULL OR f.DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR f.GeneroID = @GenreId)
            GROUP BY d.DepartamentoID, d.Nombre
            ORDER BY d.DepartamentoID;
            """, connection);
        AddFinanceFilters(command, year, departmentId, genreId, referenceDate);

        var points = new List<RevenuePoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            points.Add(new RevenuePoint(reader.GetString(1), reader.GetDecimal(2), id == 1 ? "#FFC93C" : "#1F9D55"));
        }

        return points;
    }

    private static async Task<(int Active, int Registered, IReadOnlyList<PlanUsersPoint> Plans)> ReadUsersAsync(
        SqlConnection connection, int? departmentId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN Estado = N'Activo' THEN 1 ELSE 0 END), 0)
            FROM dbo.Usuarios
            WHERE FechaRegistro <= @ReferenceDate
              AND (@DepartmentId IS NULL OR DepartamentoID = @DepartmentId);

            SELECT p.PlanID, p.Nombre, COUNT(*)
            FROM dbo.Usuarios AS u
            INNER JOIN dbo.Planes AS p ON p.PlanID = u.PlanID
            WHERE u.FechaRegistro <= @ReferenceDate
              AND (@DepartmentId IS NULL OR u.DepartamentoID = @DepartmentId)
            GROUP BY p.PlanID, p.Nombre
            ORDER BY p.PlanID;
            """, connection);
        AddDate(command, "@ReferenceDate", referenceDate);
        AddNullableInt(command, "@DepartmentId", departmentId);

        int active;
        int registered;
        var plans = new List<PlanUsersPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        registered = reader.GetInt32(0);
        active = reader.GetInt32(1);
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var planId = reader.GetInt32(0);
            plans.Add(new PlanUsersPoint(reader.GetString(1), reader.GetInt32(2), PlanColors[(planId - 1) % PlanColors.Length]));
        }

        return (active, registered, plans);
    }

    private static async Task<(int Plays, decimal Minutes, IReadOnlyList<TopSongPoint> Songs)> ReadListeningAsync(
        SqlConnection connection, int year, int? departmentId, int? genreId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        var firstDay = new DateOnly(year, 1, 1);
        await using var command = new SqlCommand("""
            SELECT COUNT(*), COALESCE(SUM(r.MinutosEscuchados), 0)
            FROM dbo.Reproducciones AS r
            INNER JOIN dbo.Canciones AS c ON c.CancionID = r.CancionID
            WHERE r.FechaReproduccion >= @FirstDay
              AND r.FechaReproduccion <= @ReferenceDate
              AND c.FechaEstreno <= @ReferenceDate
              AND (@DepartmentId IS NULL OR r.DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR c.GeneroID = @GenreId);

            SELECT TOP (6) c.Titulo, a.Nombre, COUNT(*), SUM(r.MinutosEscuchados)
            FROM dbo.Reproducciones AS r
            INNER JOIN dbo.Canciones AS c ON c.CancionID = r.CancionID
            INNER JOIN dbo.Artistas AS a ON a.ArtistaID = c.ArtistaID
            WHERE r.FechaReproduccion >= @FirstDay
              AND r.FechaReproduccion <= @ReferenceDate
              AND c.FechaEstreno <= @ReferenceDate
              AND (@DepartmentId IS NULL OR r.DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR c.GeneroID = @GenreId)
            GROUP BY c.Titulo, a.Nombre
            ORDER BY COUNT(*) DESC, c.Titulo;
            """, connection);
        AddDate(command, "@FirstDay", firstDay);
        AddDate(command, "@ReferenceDate", referenceDate);
        AddNullableInt(command, "@DepartmentId", departmentId);
        AddNullableInt(command, "@GenreId", genreId);

        int plays;
        decimal minutes;
        var songs = new List<TopSongPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        plays = reader.GetInt32(0);
        minutes = reader.GetDecimal(1);
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            songs.Add(new TopSongPoint(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDecimal(3)));
        }

        return (plays, minutes, songs);
    }

    private static async Task<IReadOnlyList<NewReleasePoint>> ReadNewReleasesAsync(
        SqlConnection connection, DateOnly start, DateOnly end, int? departmentId, int? genreId,
        DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (12) c.Titulo, a.Nombre, g.Nombre, c.FechaEstreno, a.ColorHex
            FROM dbo.Canciones AS c
            INNER JOIN dbo.Artistas AS a ON a.ArtistaID = c.ArtistaID
            INNER JOIN dbo.Generos AS g ON g.GeneroID = c.GeneroID
            WHERE c.FechaEstreno >= @StartDate
              AND c.FechaEstreno <= @EndDate
              AND c.FechaEstreno <= @ReferenceDate
              AND (@DepartmentId IS NULL OR a.DepartamentoID = @DepartmentId)
              AND (@GenreId IS NULL OR c.GeneroID = @GenreId)
            ORDER BY c.FechaEstreno DESC, c.Titulo;
            """, connection);
        AddDate(command, "@StartDate", start);
        AddDate(command, "@EndDate", end);
        AddDate(command, "@ReferenceDate", referenceDate);
        AddNullableInt(command, "@DepartmentId", departmentId);
        AddNullableInt(command, "@GenreId", genreId);

        var releases = new List<NewReleasePoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var color = reader.GetString(4);
            if (!IsHexColor(color))
            {
                color = "#35E0FF";
            }

            releases.Add(new NewReleasePoint(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)), color));
        }

        return releases;
    }

    private static void AddFinanceFilters(SqlCommand command, int year, int? departmentId, int? genreId, DateOnly referenceDate)
    {
        command.Parameters.Add("@Year", SqlDbType.Int).Value = year;
        AddDate(command, "@ReferenceDate", referenceDate);
        AddNullableInt(command, "@DepartmentId", departmentId);
        AddNullableInt(command, "@GenreId", genreId);
    }

    private static void AddDate(SqlCommand command, string name, DateOnly value) =>
        command.Parameters.Add(name, SqlDbType.Date).Value = value.ToDateTime(TimeOnly.MinValue);

    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value is null ? DBNull.Value : value.Value;

    private static bool IsHexColor(string color) =>
        color.Length == 7 && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
