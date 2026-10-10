namespace GameHub.Models;

public sealed record PlayerProfile(
    string Id,
    string Name,
    long Level,
    string Region,
    long CreatedAt);

public sealed record LeaderboardEntry(
    string PlayerId,
    double Score,
    long Rank);
