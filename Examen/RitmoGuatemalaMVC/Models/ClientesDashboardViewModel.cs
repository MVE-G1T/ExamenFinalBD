namespace RitmoGuatemalaMVC.Models;

public sealed class ClientesDashboardViewModel
{
    public required DateOnly ReferenceDate { get; init; }
    public required int SelectedYear { get; init; }
    public int? SelectedDepartmentId { get; init; }
    public int? SelectedGenreId { get; init; }
    public required DateOnly ReleaseStart { get; init; }
    public required DateOnly ReleaseEnd { get; init; }
    public required IReadOnlyList<int> Years { get; init; }
    public required IReadOnlyList<DashboardFilterOption> Departments { get; init; }
    public required IReadOnlyList<DashboardFilterOption> Genres { get; init; }
    public required DashboardKpis Kpis { get; init; }
    public required IReadOnlyList<MonthlyRevenuePoint> MonthlyRevenue { get; init; }
    public required IReadOnlyList<RevenuePoint> RevenueByGenre { get; init; }
    public required IReadOnlyList<RevenuePoint> RevenueByDepartment { get; init; }
    public required IReadOnlyList<PlanUsersPoint> UsersByPlan { get; init; }
    public required IReadOnlyList<TopSongPoint> TopSongs { get; init; }
    public required IReadOnlyList<NewReleasePoint> NewReleases { get; init; }
}

public sealed record DashboardFilterOption(int Id, string Name);
public sealed record DashboardKpis(decimal Revenue, int NewSubscriptions, int ActiveUsers, int RegisteredUsers, decimal MinutesListened, int Plays);
public sealed record MonthlyRevenuePoint(string Month, decimal? SubscriptionRevenue, decimal? AdvertisingRevenue);
public sealed record RevenuePoint(string Label, decimal Amount, string Color);
public sealed record PlanUsersPoint(string Label, int Users, string Color);
public sealed record TopSongPoint(string Title, string Artist, int Plays, decimal Minutes);
public sealed record NewReleasePoint(string Title, string Artist, string Genre, DateOnly ReleaseDate, string Color);
